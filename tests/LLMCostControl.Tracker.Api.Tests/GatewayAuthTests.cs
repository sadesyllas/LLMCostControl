using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LLMCostControl.Tracker.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Integration tests for gateway OAuth/JWKS validation (M12, §6.1). Uses a
/// mock OIDC server that serves a discovery document and JWKS, and a
/// <see cref="WebApplicationFactory{Program}"/> with overridden
/// <see cref="GatewayAuthOptions"/>.
/// </summary>
public sealed class GatewayAuthTests : IClassFixture<AuthWebAppFactory>
{
    private readonly AuthWebAppFactory _factory;

    public GatewayAuthTests(AuthWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static HttpRequestMessage AuthorizedRequest(string token, string path = "/api/auth/test")
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Valid_token_returns_200()
    {
        var token = _factory.JwtHelper.GenerateToken();
        var req = AuthorizedRequest(token);

        var resp = await CreateClient().SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_token_returns_401()
    {
        var resp = await CreateClient().GetAsync("/api/auth/test");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        var token = _factory.JwtHelper.GenerateToken(
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-10));

        var resp = await CreateClient().SendAsync(AuthorizedRequest(token));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bad_signature_token_returns_401()
    {
        var token = _factory.JwtHelper.GenerateBadSignatureToken();

        var resp = await CreateClient().SendAsync(AuthorizedRequest(token));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_issuer_returns_401()
    {
        var token = _factory.JwtHelper.GenerateToken(issuer: "https://wrong-issuer.local");

        var resp = await CreateClient().SendAsync(AuthorizedRequest(token));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_audience_returns_401()
    {
        var token = _factory.JwtHelper.GenerateToken(audience: "wrong-audience");

        var resp = await CreateClient().SendAsync(AuthorizedRequest(token));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// Web application factory that overrides <see cref="GatewayAuthOptions"/> to
/// point at the mock OIDC server and starts the mock server before the app
/// runs.
/// </summary>
public sealed class AuthWebAppFactory : WebApplicationFactory<Program>
{
    private MockOidcServer? _oidcServer;

    /// <summary>The JWT test helper used to issue tokens.</summary>
    public JwtTestHelper JwtHelper { get; } = new();

    /// <summary>The mock OIDC server serving discovery + JWKS.</summary>
    public MockOidcServer OidcServer => _oidcServer!;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _oidcServer = new MockOidcServer(JwtHelper);
        _oidcServer.Start();

        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GatewayAuth:JwksEndpoint"] = _oidcServer.DiscoveryUrl,
                ["GatewayAuth:Issuer"] = _oidcServer.Issuer,
                ["GatewayAuth:Audience"] = _oidcServer.Audience,
                // Override the connection string so the test silo uses
                // localhost clustering + memory storage (no DB needed).
                ["Orleans:StorageConnectionString"] = "",
                ["Observability:TelemetryPepper"] = "test-telemetry-pepper",
            });
        });

        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _oidcServer?.Dispose();
            JwtHelper.Dispose();
        }

        base.Dispose(disposing);
    }
}
