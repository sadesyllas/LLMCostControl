using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Components;
using LLMCostControl.Admin.App.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// Verifies the role-based and authentication-gated rendering requirements of the Admin App (M16, §12.2).
/// Asserts that anonymous users are redirected to Entra ID, and that app roles (Admin vs Read-Only) gate write controls.
/// </summary>
public class AuthGatedRenderingTests : TestContext
{
    /// <summary>
    /// Verifies that an unauthenticated (anonymous) user is redirected to the sign-in endpoint.
    /// </summary>
    [Fact]
    public void AnonymousUser_IsRedirectedToSignIn()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetNotAuthorized();

        // Act
        var cut = RenderComponent<Routes>();

        // Assert
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("MicrosoftIdentity/Account/SignIn");
    }

    /// <summary>
    /// Verifies that a user in the Read-Only role is shown read-only features but cannot see or access administrative actions.
    /// </summary>
    [Fact]
    public void ReadOnlyUser_CannotSeeAdminActions()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("readonly@example.com");
        authContext.SetRoles("CostTracker.ReadOnly");

        // Act
        var cut = RenderComponent<Home>();

        // Assert
        cut.Find("#readonly-actions").Should().NotBeNull();
        cut.FindAll("#admin-actions").Should().BeEmpty();
        cut.Markup.Should().Contain("Administrative Controls Locked");
        cut.Markup.Should().Contain("Read-Only");
    }

    /// <summary>
    /// Verifies that a user in the Admin role is shown administrative action buttons.
    /// </summary>
    [Fact]
    public void AdminUser_CanSeeAdminActions()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // Act
        var cut = RenderComponent<Home>();

        // Assert
        cut.Find("#readonly-actions").Should().NotBeNull();
        cut.Find("#admin-actions").Should().NotBeNull();
        cut.Markup.Should().NotContain("Administrative Controls Locked");
        cut.Markup.Should().Contain("Administrator");
    }
}
