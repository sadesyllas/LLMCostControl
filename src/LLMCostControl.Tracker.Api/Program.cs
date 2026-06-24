var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LLMCostControl Tracker API");

app.Run();
