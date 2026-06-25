using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Test authentication handler that creates a <see cref="ClaimsPrincipal"/>
/// from custom HTTP headers, bypassing the real EntraID OIDC flow. Used by
/// <see cref="AdminAppFactory"/> to simulate authenticated users in tests.
/// <para>
/// To authenticate a test request, set the following headers on the client:
/// <list type="bullet">
/// <item><c>X-Test-User</c>: the user's name / email (any non-empty string).</item>
/// <item><c>X-Test-Roles</c>: comma-separated role names (e.g.
/// <c>CostTracker.Admin</c>).</item>
/// </list>
/// Requests without <c>X-Test-User</c> are treated as anonymous.
/// </para>
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Header name carrying the test user's identity.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>Header name carrying comma-separated role names.</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>Authentication scheme name for test use.</summary>
    public const string SchemeName = "TestScheme";

    /// <inheritdoc />
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userHeader) ||
            string.IsNullOrWhiteSpace(userHeader.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userName = userHeader.ToString();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, userName),
        };

        if (Request.Headers.TryGetValue(RolesHeader, out var rolesHeader))
        {
            foreach (var role in rolesHeader.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
