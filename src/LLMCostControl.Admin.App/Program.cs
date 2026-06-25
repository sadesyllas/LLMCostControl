using LLMCostControl.Admin.App.Components;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
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
    
    // Register the scoped DbContext resolved from the factory
    builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<CostTrackerDbContext>>().CreateDbContext());
    
    // Register Repositories
    builder.Services.AddScoped<GroupRepository>();
    builder.Services.AddScoped<GroupBudgetRepository>();
    builder.Services.AddScoped<GroupMembershipRepository>();
    builder.Services.AddScoped<UserBudgetOverrideRepository>();
    builder.Services.AddScoped<ModelPricingRepository>();
    builder.Services.AddScoped<UsageEventRepository>();
    builder.Services.AddScoped<BudgetResolutionRepository>();
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
