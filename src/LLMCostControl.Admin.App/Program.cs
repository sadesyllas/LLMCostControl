using LLMCostControl.Admin.App.Auth;
using LLMCostControl.Admin.App.Components;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Admin.App");
builder.ConfigureObservability("LLMCostControl.Admin.App");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register shared Infrastructure repositories (§12.1: admin app reads/writes
// Postgres via shared repositories, does NOT host Orleans or call grains).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContextFactory<CostTrackerDbContext>(options =>
        options.UseNpgsql(connectionString));
}

builder.Services.AddScoped<IAdminCommandService, AdminCommandService>();

// EntraID (Azure AD) OIDC authentication (§12.2).
var authOptions = builder.Configuration.GetSection("EntraId").Get<AdminAuthOptions>() ?? new();

if (authOptions.IsEnabled)
{
    builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "LLMCostControlAdmin";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = authOptions.Authority;
        o.ClientId = authOptions.ClientId;
        o.ClientSecret = authOptions.ClientSecret;
        o.ResponseType = "code";
        o.SaveTokens = true;
        o.UseTokenLifetime = true;
        o.TokenValidationParameters.RoleClaimType = "roles";
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(AppRoles.Admin, policy => policy.RequireRole(AppRoles.Admin));
        options.AddPolicy(AppRoles.ReadOnly, policy => policy.RequireRole(AppRoles.ReadOnly));
    });
}
else
{
    builder.Services.AddAuthorization();
}

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

if (authOptions.IsEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Login / logout endpoints (server-side HTTP redirects, not Blazor components).
if (authOptions.IsEnabled)
{
    app.MapGet("/login", async (HttpContext context, [FromQuery] string? returnUrl) =>
    {
        returnUrl ??= "/";
        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = returnUrl });
    });

    app.MapGet("/logout", async (HttpContext context) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = "/" });
    });
}

app.Run();
