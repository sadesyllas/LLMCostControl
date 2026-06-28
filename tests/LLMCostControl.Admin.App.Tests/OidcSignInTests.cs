using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Integration test for the EntraID sign-in flow (M16, §12.2). Boots the admin
/// app with a stubbed OIDC provider and verifies that an anonymous request to a
/// protected page issues an OpenID Connect challenge that redirects to the
/// provider's authorization endpoint — exercising the real OIDC middleware,
/// unlike the bUnit auth-gating component tests.
/// </summary>
public sealed class OidcSignInTests : IClassFixture<AdminAppFactory>
{
    private readonly AdminAppFactory _factory;

    public OidcSignInTests(AdminAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_request_is_challenged_to_the_stubbed_oidc_provider()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        // The fallback policy (§12.2) requires authentication for every endpoint,
        // so an anonymous request to the home page must trigger an OIDC challenge.
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.ToString().Should().StartWith(AdminAppFactory.AuthorizationEndpoint,
            "an unauthenticated request must be redirected to the configured identity provider.");
    }
}

/// <summary>
/// Web application factory for the admin app that supplies an EntraID app
/// registration (so OIDC is configured) and stubs the provider metadata so the
/// challenge resolves without contacting a live identity provider. Also supplies
/// the required <c>TelemetryPepper</c> so the host passes its fail-fast config
/// check (§10.2).
/// </summary>
public sealed class AdminAppFactory : WebApplicationFactory<Program>
{
    /// <summary>The stubbed provider authorization endpoint the challenge redirects to.</summary>
    public const string AuthorizationEndpoint = "https://stub-idp.example/authorize";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                ["AzureAd:CallbackPath"] = "/signin-oidc",
                // Required by the fail-fast telemetry pepper check (§10.2).
                ["Observability:TelemetryPepper"] = "test-pepper-not-for-production",
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
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = false;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration
                    {
                        Issuer = "https://stub-idp.example/",
                        AuthorizationEndpoint = AuthorizationEndpoint,
                        TokenEndpoint = "https://stub-idp.example/token",
                        EndSessionEndpoint = "https://stub-idp.example/logout",
                        JwksUri = "https://stub-idp.example/jwks",
                    });
            });
        });
    }
}
