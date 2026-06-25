using LLMCostControl.Admin.App.Auth;
using LLMCostControl.Admin.App.Components;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Admin.App");
builder.ConfigureObservability("LLMCostControl.Admin.App");

// The admin app accesses PostgreSQL via the shared Infrastructure repositories
// (M4) — never via Orleans grains (§12.1). Registered only when a connection
// string is configured so the app still boots in dev/tests without a database.
var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContextFactory<CostTrackerDbContext>(options =>
        options.UseNpgsql(connectionString));
}

// Admin command handlers (§12.3) — drive the shared repositories, never grains.
builder.Services.AddScoped<IAdminCommandService, AdminCommandService>();

// EntraID (Azure AD) OpenID Connect sign-in (§12.2). The admin app's auth is
// completely independent of the tracker's gateway token; it never accepts that
// token. Auth is enabled only when an AzureAd app registration is configured so
// the app can still boot in plain local dev without EntraID.
var azureAd = builder.Configuration.GetSection("AzureAd");
var authEnabled = !string.IsNullOrWhiteSpace(azureAd["ClientId"]);

if (authEnabled)
{
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(azureAd);
}

// Role-based authorization (§12.2): EntraID app roles CostTracker.Admin /
// CostTracker.ReadOnly. Policies are registered regardless of auth being wired
// so component markup (and tests) can reference them by name.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAuthorization.AdminPolicy, policy =>
        policy.RequireRole(AdminAuthorization.AdminRole));
    options.AddPolicy(AdminAuthorization.ReadOnlyPolicy, policy =>
        policy.RequireRole(AdminAuthorization.AdminRole, AdminAuthorization.ReadOnlyRole));
});

builder.Services.AddCascadingAuthenticationState();

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

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (authEnabled)
{
    // Minimal sign-in / sign-out endpoints (§12.2) — no MVC UI package required.
    app.MapGet("/authentication/login", (string? returnUrl) =>
        TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl },
            [OpenIdConnectDefaults.AuthenticationScheme]));

    app.MapPost("/authentication/logout", () =>
        TypedResults.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
}

app.Run();

/// <summary>Entry point marker so <c>WebApplicationFactory&lt;Program&gt;</c> can host the app in tests.</summary>
public partial class Program;
