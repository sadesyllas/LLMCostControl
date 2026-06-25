using System.Net;
using System.Net.Http.Headers;
using LLMCostControl.Admin.App.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Integration tests for M16 EntraID auth-gated rendering (§12.1, §12.2).
/// Uses <see cref="AdminAppFactory"/> which overrides authentication to use the
/// test scheme (<see cref="TestAuthHandler"/>) so OIDC is not needed.
/// </summary>
public sealed class AdminAuthTests : IClassFixture<AdminAppFactory>
{
    private readonly AdminAppFactory _factory;

    public AdminAuthTests(AdminAppFactory factory) => _factory = factory;

    private HttpClient Anonymous() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private HttpClient AsAdmin()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "admin@example.com");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, AppRoles.Admin);
        return client;
    }

    private HttpClient AsReadOnly()
    {
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "viewer@example.com");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, AppRoles.ReadOnly);
        return client;
    }

    [Fact]
    public async Task Anonymous_request_to_blazor_route_is_rejected()
    {
        var resp = await Anonymous().GetAsync("/Admin/Dashboard");
        resp.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Redirect],
            "unauthenticated requests must be challenged");
    }

    [Fact]
    public async Task Admin_user_can_access_admin_dashboard()
    {
        var resp = await AsAdmin().GetAsync("/Admin/Dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("admin-dashboard-heading",
            because: "admin user should see the admin dashboard content");
    }

    [Fact]
    public async Task Admin_user_can_access_read_only_reports_page()
    {
        var resp = await AsAdmin().GetAsync("/Reports");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("reports-heading",
            because: "admin has access to all read-only views");
    }

    [Fact]
    public async Task ReadOnly_user_can_access_reports_page()
    {
        var resp = await AsReadOnly().GetAsync("/Reports");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("reports-heading",
            because: "read-only user should see the reports page");
    }

    [Fact]
    public async Task ReadOnly_user_cannot_access_admin_dashboard()
    {
        var resp = await AsReadOnly().GetAsync("/Admin/Dashboard");
        // Blazor Web App enforces component-level [Authorize] during static
        // pre-rendering, so wrong-role requests result in HTTP 403 at the
        // middleware level (not a 200 with NotAuthorized template content).
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: "read-only user does not have the Admin role");
    }
}

/// <summary>
/// Web application factory for M16 auth tests. Overrides authentication to use
/// <see cref="TestAuthHandler"/> instead of the real EntraID OIDC, and supplies
/// dummy AzureAd config to prevent startup errors.
/// </summary>
public sealed class AdminAppFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant",
                ["AzureAd:ClientId"] = "test-client",
                ["AzureAd:ClientSecret"] = "test-secret",
            });
        });

        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace the production OIDC + Cookie scheme with the test handler.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.Configure<AuthenticationOptions>(opts =>
            {
                opts.DefaultScheme = TestAuthHandler.SchemeName;
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                opts.DefaultSignInScheme = TestAuthHandler.SchemeName;
            });
        });
    }
}
