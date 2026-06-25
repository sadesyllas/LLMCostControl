using LLMCostControl.Admin.App.Auth;
using LLMCostControl.Admin.App.Components;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using LLMCostControl.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseObservability("LLMCostControl.Admin.App");
builder.ConfigureObservability("LLMCostControl.Admin.App");

var azureAd = builder.Configuration.GetSection("AzureAd");

// EntraID (Azure AD) OIDC sign-in + cookie session (§12.2).
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    var instance = azureAd["Instance"] ?? "https://login.microsoftonline.com/";
    var tenantId = azureAd["TenantId"] ?? "common";
    options.Authority = $"{instance.TrimEnd('/')}/{tenantId}/v2.0";
    options.ClientId = azureAd["ClientId"];
    options.ClientSecret = azureAd["ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = false;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = false,
        NameClaimType = "preferred_username",
        RoleClaimType = "roles",
    };
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
});

// Role-based authorization policies (§12.2).
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppRoles.AdminPolicy, policy =>
        policy.RequireRole(AppRoles.Admin));
    options.AddPolicy(AppRoles.ReadOnlyPolicy, policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.ReadOnly));
});

// Postgres via shared repositories (§12.1, M17).
var dbConnStr = builder.Configuration["Database:ConnectionString"];
if (!string.IsNullOrWhiteSpace(dbConnStr))
{
    builder.Services.AddDbContext<CostTrackerDbContext>(opts =>
        opts.UseNpgsql(dbConnStr));
    builder.Services.AddScoped<GroupRepository>();
    builder.Services.AddScoped<GroupMembershipRepository>();
    builder.Services.AddScoped<GroupBudgetRepository>();
    builder.Services.AddScoped<UserBudgetOverrideRepository>();
    builder.Services.AddScoped<IGroupAdminService, GroupAdminService>();
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Provides auth state to Blazor components via CascadingAuthenticationState.
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Minimal account endpoints for sign-in / sign-out.
app.MapGet("/Account/Login", async (string? returnUrl, HttpContext ctx) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" });
}).AllowAnonymous();

app.MapGet("/Account/Logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(); // All Blazor pages require authentication.

app.Run();

/// <summary>Partial class declaration required by WebApplicationFactory.</summary>
public partial class Program { }
