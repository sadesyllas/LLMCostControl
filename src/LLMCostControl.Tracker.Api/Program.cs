using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Implementations;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Publishers;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Observability;
using LLMCostControl.Tracker.Api.Auth;
using LLMCostControl.Tracker.Api.Endpoints;
using LLMCostControl.Tracker.Api.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability(TrackerTelemetry.ServiceName);
builder.ConfigureObservability(TrackerTelemetry.ServiceName);

var orleansConfig = builder.Configuration.GetSection("Orleans");
var adoInvariant = "Npgsql";
var connectionString = orleansConfig["StorageConnectionString"];

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContextFactory<CostTrackerDbContext>(options =>
        options.UseNpgsql(connectionString));
}

builder.Services.AddScoped<IPricingStore, PricingStore>();
builder.Services.AddScoped<IBudgetStore, BudgetStore>();
builder.Services.AddScoped<IUsageEventStore, UsageEventStore>();
builder.Services.AddSingleton<IPricingCache, PricingCache>();
builder.Services.AddSingleton<IPricingWriter, DbPricingWriter>();
builder.Services.AddSingleton<PricingImportService>();
builder.Services.AddSingleton<TrackerTelemetry>();
builder.Services.Configure<BudgetGrainOptions>(builder.Configuration.GetSection("BudgetGrain"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BudgetGrainOptions>>().Value);
builder.Services.AddSingleton(TimeProvider.System);

// Gateway authentication (§6.1): JWT bearer with configurable JWKS, issuer, audience.
builder.Services.Configure<GatewayAuthOptions>(builder.Configuration.GetSection("GatewayAuth"));
var authOptions = builder.Configuration.GetSection("GatewayAuth").Get<GatewayAuthOptions>() ?? new();

if (authOptions.IsEnabled)
{
    var authBuilder = builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            jwt.MetadataAddress = authOptions.JwksEndpoint;
            jwt.RequireHttpsMetadata = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(authOptions.Issuer),
                ValidIssuer = authOptions.Issuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(authOptions.Audience),
                ValidAudience = authOptions.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });

    builder.Services.AddAuthorization();
}

builder.Host.UseOrleans(silo =>
{
    silo.ConfigureLogging(logging => logging.AddFilter("Orleans", LogLevel.Warning));

    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        silo.AddAdoNetGrainStorage("Default", options =>
        {
            options.ConnectionString = connectionString;
            options.Invariant = adoInvariant;
        });
        silo.AddAdoNetGrainStorage("PubSubStore", options =>
        {
            options.ConnectionString = connectionString;
            options.Invariant = adoInvariant;
        });
    }
    else
    {
        silo.UseLocalhostClustering();
        silo.AddMemoryGrainStorage("Default");
        silo.AddMemoryGrainStorage("PubSubStore");
    }

    silo.AddMemoryStreams("pricing");
});

builder.Services.AddSingleton<IPricingUpdatePublisher, OrleansPricingPublisher>();

var app = builder.Build();

if (authOptions.IsEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/", () => "LLMCostControl Tracker API");

// M12 test endpoint: requires a valid gateway token.
if (authOptions.IsEnabled)
{
    app.MapGet("/api/auth/test", () => new { ok = true })
       .RequireAuthorization();
}

// M13: Budget check endpoint (§6.2.1).
app.MapPost("/api/budget/check", async (
    BudgetCheckRequest request,
    IGrainFactory grainFactory,
    TrackerTelemetry telemetry,
    ILoggerFactory loggerFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.CallerId))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "callerId is required." });
    }

    var grain = grainFactory.GetGrain<IUserBudgetGrain>(request.CallerId);
    var result = await grain.CheckBudgetAsync();

    // M15: tag span/logs and record the metric with the effective-group budget context (§10.2).
    using var groupScope = telemetry.EnterEffectiveGroupScope(result.EffectiveGroupId, result.BudgetSource);
    telemetry.RecordBudgetCheck(result.Allowed, result.EffectiveGroupId, result.BudgetSource);
    loggerFactory.CreateLogger("LLMCostControl.Tracker.Api.BudgetCheck")
        .LogInformation("Budget check for {CallerId} decided allowed={Allowed}.", result.CallerId, result.Allowed);

    var response = new BudgetCheckResponse
    {
        Allowed = result.Allowed,
        CallerId = result.CallerId,
        EffectiveBudget = result.EffectiveBudgetAmount is null
            ? null
            : new MoneyDto { Amount = result.EffectiveBudgetAmount.Value, Currency = result.EffectiveBudgetCurrency ?? "USD" },
        RunningSpend = new MoneyDto { Amount = result.RunningSpendAmount, Currency = result.RunningSpendCurrency },
        Remaining = new MoneyDto { Amount = result.RemainingAmount, Currency = result.RemainingCurrency },
    };

    return Results.Ok(response);
}).RequireAuthorization();

// M13: Usage capture endpoint (§6.2.2).
app.MapPost("/api/usage/capture", async (
    UsageCaptureDto request,
    IGrainFactory grainFactory,
    TrackerTelemetry telemetry,
    ILoggerFactory loggerFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.CallerId))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "callerId is required." });
    }

    if (string.IsNullOrWhiteSpace(request.Model))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "model is required." });
    }

    var grain = grainFactory.GetGrain<IUserBudgetGrain>(request.CallerId);

    try
    {
        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = request.Model,
            TokensInput = request.Tokens.Input,
            TokensOutput = request.Tokens.Output,
            TokensCacheRead = request.Tokens.CacheRead,
            TokensCacheWrite = request.Tokens.CacheWrite,
            RequestId = request.RequestId,
        });

        // M15: tag span/logs and record metrics with the effective-group budget context (§10.2).
        using var groupScope = telemetry.EnterEffectiveGroupScope(result.EffectiveGroupId, result.BudgetSource);
        telemetry.RecordUsageCapture(result.CostAmount, result.EffectiveGroupId, result.BudgetSource);
        loggerFactory.CreateLogger("LLMCostControl.Tracker.Api.UsageCapture")
            .LogInformation(
                "Usage capture for {CallerId} on {Model} accrued {Cost} {Currency}.",
                result.CallerId, request.Model, result.CostAmount, result.CostCurrency);

        var response = new UsageCaptureResponse
        {
            CallerId = result.CallerId,
            Cost = new MoneyDto { Amount = result.CostAmount, Currency = result.CostCurrency },
            RunningSpend = new MoneyDto { Amount = result.RunningSpendAmount, Currency = result.RunningSpendCurrency },
            Remaining = new MoneyDto { Amount = result.RemainingAmount, Currency = result.RemainingCurrency },
        };

        return Results.Ok(response);
    }
    catch (UnknownModelException ex)
    {
        return Results.BadRequest(new ErrorResponse { Error = "unknown_model", Detail = ex.Message });
    }
}).RequireAuthorization();

// M14: Localhost-only pricing file import endpoint (§8.3). Bound to loopback
// callers via LocalhostOnlyEndpointFilter; feeds the uploaded file through the
// shared validator (M5) and the refresh job's write path (M7). No gateway token
// is required — localhost binding is the trust boundary.
app.MapPost("/api/pricing/import", async (
    HttpContext httpContext,
    PricingImportService importService,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var content = await reader.ReadToEndAsync(ct);

    var result = await importService.ImportAsync(content, ct);

    if (!result.Success)
    {
        return Results.BadRequest(new PricingImportErrorResponse
        {
            Error = "invalid_pricing_file",
            Errors = result.Errors,
        });
    }

    return Results.Ok(new PricingImportResponse
    {
        ImportedModelCount = result.ImportedModelCount,
        AffectedProviders = result.AffectedProviders.Select(p => p.ToString()).ToList(),
    });
}).AddEndpointFilter(new LocalhostOnlyEndpointFilter());

app.Run();
