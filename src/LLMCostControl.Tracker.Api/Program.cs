using LLMCostControl.Grains.Implementations;
using LLMCostControl.Grains.Options;
using LLMCostControl.Grains.Publishers;
using LLMCostControl.Grains.Storage;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Observability;
using Microsoft.EntityFrameworkCore;

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
builder.Services.Configure<BudgetGrainOptions>(builder.Configuration.GetSection("BudgetGrain"));
builder.Services.AddSingleton(TimeProvider.System);

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
        silo.AddMemoryGrainStorage("Default");
        silo.AddMemoryGrainStorage("PubSubStore");
    }

    silo.AddMemoryStreams("pricing");
});

builder.Services.AddSingleton<IPricingUpdatePublisher, OrleansPricingPublisher>();

var app = builder.Build();

app.MapGet("/", () => "LLMCostControl Tracker API");

app.Run();
