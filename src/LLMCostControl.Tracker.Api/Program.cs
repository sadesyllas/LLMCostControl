using System.Diagnostics;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Grains.Abstractions;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Publishers;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Observability;
using LLMCostControl.Tracker.Api.Auth;
using LLMCostControl.Tracker.Api.Endpoints;
using LLMCostControl.Tracker.Api.Observability;
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

// Provider-inference map (prefix -> provider) used to resolve the provider when a
// call omits it (§6.2.3). Filled from the Pricing:ProviderInference config section
// (appsettings.json), not hard-coded.
builder.Services.AddSingleton(_ => new ProviderInferenceMap(
    builder.Configuration.GetSection("Pricing:ProviderInference").Get<Dictionary<string, string>>()
        ?? new Dictionary<string, string>()));

// Gateway authentication (§6.1): JWT bearer with configurable JWKS, issuer, audience.
builder.Services.Configure<GatewayAuthOptions>(builder.Configuration.GetSection("GatewayAuth"));
var authOptions = builder.Configuration.GetSection("GatewayAuth").Get<GatewayAuthOptions>() ?? new();

if (authOptions.IsEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            jwt.MetadataAddress = authOptions.JwksEndpoint;
            jwt.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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
builder.Services.AddHostedService<PricingStreamSubscriber>();
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

// M13: Budget check endpoint (§6.2.1).
app.MapPost("/api/budget/check", async (
    BudgetCheckRequest request,
    IGrainFactory grainFactory,
    TrackerMetrics trackerMetrics) =>
{
    if (string.IsNullOrWhiteSpace(request.CallerId))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "callerId is required." });
    }

    var grain = grainFactory.GetGrain<IUserBudgetGrain>(request.CallerId);
    var result = await grain.CheckBudgetAsync();

    var budgetSourceStr = result.BudgetSource.ToString();
    var effectiveGroupStr = result.EffectiveGroupId?.ToString() ?? "None";

    Activity.Current.SetTelemetryTags(request.CallerId, effectiveGroupStr, budgetSourceStr);

    trackerMetrics.RecordBudgetCheck(request.CallerId, result.Allowed ? "allowed" : "denied", effectiveGroupStr, budgetSourceStr);

    using (Serilog.Context.LogContext.PushProperty("effective_group", effectiveGroupStr))
    using (Serilog.Context.LogContext.PushProperty("budget_source", budgetSourceStr))
    {
        app.Logger.LogInformation("Budget check for {CallerId}: allowed={Allowed}, source={Source}, group={Group}",
            ObservabilityExtensions.AnonymizeCallerId(request.CallerId), result.Allowed, budgetSourceStr, effectiveGroupStr);
    }

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
    TrackerMetrics trackerMetrics) =>
{
    if (string.IsNullOrWhiteSpace(request.CallerId))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "callerId is required." });
    }

    if (string.IsNullOrWhiteSpace(request.Model))
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "model is required." });
    }

    if (request.Tokens.Input < 0 || request.Tokens.Output < 0 || request.Tokens.CacheRead < 0 || request.Tokens.CacheWrite < 0)
    {
        return Results.BadRequest(new ErrorResponse { Error = "invalid_request", Detail = "Token counts cannot be negative." });
    }

    var grain = grainFactory.GetGrain<IUserBudgetGrain>(request.CallerId);

    try
    {
        var result = await grain.CaptureUsageAsync(new UsageCaptureRequest
        {
            Model = request.Model,
            Provider = request.Provider,
            TokensInput = request.Tokens.Input,
            TokensOutput = request.Tokens.Output,
            TokensCacheRead = request.Tokens.CacheRead,
            TokensCacheWrite = request.Tokens.CacheWrite,
            RequestId = request.RequestId,
        });

        var budgetSourceStr = result.BudgetSource.ToString();
        var effectiveGroupStr = result.EffectiveGroupId?.ToString() ?? "None";

        Activity.Current.SetTelemetryTags(request.CallerId, effectiveGroupStr, budgetSourceStr);

        trackerMetrics.RecordUsageCapture(
            request.CallerId,
            "accepted",
            request.Model,
            result.CostAmount,
            result.CostCurrency,
            request.Tokens.Input,
            request.Tokens.Output,
            request.Tokens.CacheRead,
            request.Tokens.CacheWrite,
            effectiveGroupStr,
            budgetSourceStr);

        using (Serilog.Context.LogContext.PushProperty("effective_group", effectiveGroupStr))
        using (Serilog.Context.LogContext.PushProperty("budget_source", budgetSourceStr))
        {
            app.Logger.LogInformation("Usage captured for {CallerId}: model={Model}, cost={Cost} {Currency}, source={Source}, group={Group}",
                ObservabilityExtensions.AnonymizeCallerId(request.CallerId), request.Model, result.CostAmount, result.CostCurrency, budgetSourceStr, effectiveGroupStr);
        }

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
        Activity.Current.SetTelemetryTags(request.CallerId, "None", "None");

        trackerMetrics.RecordUsageCapture(
            request.CallerId,
            "rejected",
            request.Model,
            0m,
            "USD",
            request.Tokens.Input,
            request.Tokens.Output,
            request.Tokens.CacheRead,
            request.Tokens.CacheWrite,
            "None",
            "None");

        return Results.BadRequest(new ErrorResponse { Error = "unknown_model", Detail = ex.Message });
    }
}).RequireAuthorization();

app.Run();
