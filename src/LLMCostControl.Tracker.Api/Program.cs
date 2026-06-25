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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Tracker.Api");
builder.ConfigureObservability("LLMCostControl.Tracker.Api");

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
builder.Services.AddScoped<PricingFileImporter>();

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
    IGrainFactory grainFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.CallerId))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "callerId is required." });
    }

    var grain = grainFactory.GetGrain<IUserBudgetGrain>(request.CallerId);
    var result = await grain.CheckBudgetAsync();

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
    IGrainFactory grainFactory) =>
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

// M14: Localhost pricing file import endpoint (§8.3).
// Accepts a canonical pricing file (§8.5) as JSON, validates it via the shared
// validator (M5), persists via the refresh job's write path (M7), and publishes
// pricing-updated events so PricingGrain activations pick up new values.
// localhost-only: not reachable from a non-loopback address.
app.MapPost("/api/pricing/import", async (
    HttpContext context,
    PricingFileImporter importer) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var json = await reader.ReadToEndAsync(context.RequestAborted);

    var result = await importer.ImportAsync(json, context.RequestAborted);

    if (!result.Success)
    {
        return Results.BadRequest(new ErrorResponse
        {
            Error = "invalid_file",
            Detail = string.Join("; ", result.Errors),
        });
    }

    return Results.Ok(new PricingImportResponse
    {
        Imported = true,
        Count = result.ImportedCount,
        UpdatedModels = result.UpdatedModels,
    });
}).AddEndpointFilter(async (context, next) =>
{
    var remote = context.HttpContext.Connection.RemoteIpAddress;
    if (remote is not null && !System.Net.IPAddress.IsLoopback(remote))
    {
        return Results.Json(
            new ErrorResponse { Error = "forbidden", Detail = "This endpoint is localhost-only." },
            statusCode: StatusCodes.Status403Forbidden);
    }

    return await next(context);
});

app.Run();
