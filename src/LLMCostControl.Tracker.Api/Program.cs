using System.Diagnostics;
using System.Net;
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

builder.Host.UseObservability("LLMCostControl.Tracker.Api");
builder.ConfigureObservability("LLMCostControl.Tracker.Api");

var orleansConfig = builder.Configuration.GetSection("Orleans");
var adoInvariant = "Npgsql";
var connectionString = orleansConfig["StorageConnectionString"];

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContextFactory<CostTrackerDbContext>(options =>
        options.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IPricingStoreWriter, ModelPricingWriter>();
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
builder.Services.AddSingleton<PricingImportService>();
builder.Services.AddSingleton<TrackerMetrics>();

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

// M13: Budget check endpoint (§6.2.1). M15: tags span + log + metrics.
app.MapPost("/api/budget/check", async (
    BudgetCheckRequest request,
    IGrainFactory grainFactory,
    TrackerMetrics metrics,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.CallerId))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "callerId is required." });
    }

    var grain = grainFactory.GetGrain<IUserBudgetGrain>(request.CallerId);
    var result = await grain.CheckBudgetAsync();

    // M15: propagate effective_group + budget_source into span, log scope, and metrics.
    var effectiveGroup = result.EffectiveGroupId?.ToString() ?? "none";
    var budgetSource = result.BudgetSource.ToString();
    Activity.Current?.SetTag("effective_group", effectiveGroup);
    Activity.Current?.SetTag("budget_source", budgetSource);
    using var _ = logger.BeginScope(new Dictionary<string, object>
    {
        ["effective_group"] = effectiveGroup,
        ["budget_source"] = budgetSource,
    });
    metrics.RecordCheck(result.Allowed, budgetSource, effectiveGroup);
    logger.LogInformation("Budget check: callerId={CallerId} allowed={Allowed} budgetSource={BudgetSource} effectiveGroup={EffectiveGroup}",
        result.CallerId, result.Allowed, budgetSource, effectiveGroup);

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

// M13: Usage capture endpoint (§6.2.2). M15: tags span + log + metrics.
app.MapPost("/api/usage/capture", async (
    UsageCaptureDto request,
    IGrainFactory grainFactory,
    TrackerMetrics metrics,
    ILogger<Program> logger) =>
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

        // M15: propagate effective_group + budget_source into span, log scope, and metrics.
        var effectiveGroup = result.EffectiveGroupId?.ToString() ?? "none";
        var budgetSource = result.BudgetSource.ToString();
        Activity.Current?.SetTag("effective_group", effectiveGroup);
        Activity.Current?.SetTag("budget_source", budgetSource);
        using var _ = logger.BeginScope(new Dictionary<string, object>
        {
            ["effective_group"] = effectiveGroup,
            ["budget_source"] = budgetSource,
        });
        metrics.RecordCapture(result.CostAmount, request.Model, budgetSource, effectiveGroup);
        logger.LogInformation("Usage capture: callerId={CallerId} model={Model} cost={Cost} budgetSource={BudgetSource} effectiveGroup={EffectiveGroup}",
            result.CallerId, request.Model, result.CostAmount, budgetSource, effectiveGroup);

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

// M14: Localhost pricing file import endpoint (§8.3). No auth — restricted to
// loopback callers only. Non-loopback requests are rejected with 403.
app.MapPost("/api/pricing/import", async (HttpContext ctx, PricingImportService importService) =>
{
    var remoteIp = ctx.Connection.RemoteIpAddress;
    if (remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    using var reader = new StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync(ctx.RequestAborted);

    if (string.IsNullOrWhiteSpace(json))
        return Results.UnprocessableEntity(new ImportErrorResponse { Errors = ["Request body is required."] });

    var result = await importService.ImportAsync(json, ctx.RequestAborted);
    return result.IsSuccess
        ? Results.Ok(new ImportSuccessResponse { ImportedCount = result.ImportedCount })
        : Results.UnprocessableEntity(new ImportErrorResponse { Errors = result.Errors });
});

app.Run();
