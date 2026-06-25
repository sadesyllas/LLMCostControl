namespace LLMCostControl.Admin.App.Auth;

/// <summary>
/// EntraID app-role constants used for authorization in the admin app (§12.2).
/// Role assignment is managed in EntraID, not in the tracker's DB.
/// </summary>
public static class AppRoles
{
    /// <summary>Full admin: create/edit groups, budgets, membership, overrides.</summary>
    public const string Admin = "CostTracker.Admin";

    /// <summary>Read-only: view groups, budgets, pricing, and usage events.</summary>
    public const string ReadOnly = "CostTracker.ReadOnly";

    /// <summary>Authorization policy name for admin-only pages.</summary>
    public const string AdminPolicy = "AdminOnly";

    /// <summary>
    /// Authorization policy name for pages accessible to both admins and
    /// read-only users.
    /// </summary>
    public const string ReadOnlyPolicy = "ReadOnlyOrAdmin";
}
