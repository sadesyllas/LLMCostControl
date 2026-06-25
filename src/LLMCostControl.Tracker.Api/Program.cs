using LLMCostControl.Grains.Implementations;
using LLMCostControl.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Tracker.Api");
builder.ConfigureObservability("LLMCostControl.Tracker.Api");

var orleansConfig = builder.Configuration.GetSection("Orleans");
var adoInvariant = "Npgsql";
var connectionString = orleansConfig["StorageConnectionString"];

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
    }
    else
    {
        silo.AddMemoryGrainStorage("Default");
    }

    silo.AddMemoryStreams("pricing");

    silo.ConfigureServices(services =>
    {
        services.AddSingleton<PricingGrain>();
        services.AddSingleton<UserBudgetGrain>();
    });
});

var app = builder.Build();

app.MapGet("/", () => "LLMCostControl Tracker API");

app.Run();
