using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Auth;
using LLMCostControl.Admin.App.Components.Auth;
using LLMCostControl.Admin.App.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for role-based auth-gated rendering (M16, §12.2): the Admin role
/// sees administrative commands, a ReadOnly user does not, and an anonymous
/// visitor is redirected to sign-in.
/// </summary>
public sealed class AuthGatedRenderingTests : BunitContext
{
    [Fact]
    public void Admin_user_sees_administration_and_reports_links()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@example.com", AuthorizationState.Authorized);
        auth.SetRoles(AdminAuthorization.AdminRole);

        var cut = Render<NavMenu>();

        cut.FindAll("[data-testid=admin-nav-link]").Should().ContainSingle();
        cut.Markup.Should().Contain("Administration");
        cut.Markup.Should().Contain("Reports");
    }

    [Fact]
    public void ReadOnly_user_sees_reports_but_not_administration()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("viewer@example.com", AuthorizationState.Authorized);
        auth.SetRoles(AdminAuthorization.ReadOnlyRole);

        var cut = Render<NavMenu>();

        cut.FindAll("[data-testid=admin-nav-link]").Should().BeEmpty();
        cut.Markup.Should().NotContain("Administration");
        cut.Markup.Should().Contain("Reports");
    }

    [Fact]
    public void Anonymous_user_sees_neither_administration_nor_reports()
    {
        AddAuthorization().SetNotAuthorized();

        var cut = Render<NavMenu>();

        cut.FindAll("[data-testid=admin-nav-link]").Should().BeEmpty();
        cut.Markup.Should().NotContain("Administration");
        cut.Markup.Should().NotContain("Reports");
    }

    [Fact]
    public void Anonymous_user_is_redirected_to_sign_in()
    {
        AddAuthorization().SetNotAuthorized();

        Render<RedirectToLogin>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.Uri.Should().Contain("authentication/login");
    }
}
