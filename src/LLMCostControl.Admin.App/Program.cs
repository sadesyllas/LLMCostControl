using LLMCostControl.Admin.App.Components;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Observability;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Admin.App");
builder.ConfigureObservability("LLMCostControl.Admin.App");

// Add EF Core Database Factory & Repositories (§12.1)
var connectionString = builder.Configuration.GetSection("Orleans")["StorageConnectionString"];
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContextFactory<CostTrackerDbContext>(options =>
        options.UseNpgsql(connectionString));
}

// Add authentication & authorization (§12.2)
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services.AddAuthorization(options =>
{
    // Fallback policy: require authentication by default for all endpoints
    options.FallbackPolicy = options.DefaultPolicy;
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Exposed so integration tests can host the admin app via
/// <c>WebApplicationFactory&lt;Program&gt;</c> (§13.3).
/// </summary>
public partial class Program;
