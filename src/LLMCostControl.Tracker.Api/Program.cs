using LLMCostControl.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Tracker.Api");
builder.ConfigureObservability("LLMCostControl.Tracker.Api");

var app = builder.Build();

app.MapGet("/", () => "LLMCostControl Tracker API");

app.Run();
