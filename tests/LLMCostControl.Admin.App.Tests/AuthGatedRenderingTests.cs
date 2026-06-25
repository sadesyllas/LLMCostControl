using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Auth;
using LLMCostControl.Admin.App.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for M16 auth-gated rendering (§12.2). Verifies that the Home
/// component renders different content based on the user's EntraID app role:
/// admin sees admin commands, read-only does not, and anonymous is not
/// authorized.
/// </summary>
public sealed class AuthGatedRenderingTests : TestContext
{
    private readonly TestAuthorizationContext _auth;

    public AuthGatedRenderingTests()
    {
        _auth = this.AddTestAuthorization();
    }

    [Fact]
    public void Admin_user_sees_admin_commands()
    {
        _auth.SetAuthorized("admin@example.com");
        _auth.SetRoles(AppRoles.Admin);

        var cut = RenderComponent<Home>();

        cut.Markup.Should().Contain("Admin Commands");
        cut.Markup.Should().Contain("admin@example.com");
    }

    [Fact]
    public void ReadOnly_user_does_not_see_admin_commands()
    {
        _auth.SetAuthorized("reader@example.com");
        _auth.SetRoles(AppRoles.ReadOnly);

        var cut = RenderComponent<Home>();

        cut.Markup.Should().Contain("Read-Only Access");
        cut.Markup.Should().NotContain("Admin Commands");
    }

    [Fact]
    public void Admin_with_both_roles_sees_admin_commands()
    {
        _auth.SetAuthorized("dual@example.com");
        _auth.SetRoles(AppRoles.Admin, AppRoles.ReadOnly);

        var cut = RenderComponent<Home>();

        cut.Markup.Should().Contain("Admin Commands");
    }

    [Fact]
    public void Anonymous_user_is_not_authorized()
    {
        _auth.SetNotAuthorized();

        var cut = RenderComponent<Home>();

        cut.Markup.Should().NotContain("Welcome");
    }

    [Fact]
    public void Authenticated_user_without_known_role_sees_welcome_only()
    {
        _auth.SetAuthorized("unknown@example.com");

        var cut = RenderComponent<Home>();

        cut.Markup.Should().Contain("unknown@example.com");
        cut.Markup.Should().NotContain("Admin Commands");
        cut.Markup.Should().NotContain("Read-Only Access");
    }

    [Fact]
    public void RedirectToLogin_navigates_to_login_page()
    {
        var cut = RenderComponent<RedirectToLogin>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.Uri.Should().Contain("/login");
    }
}
