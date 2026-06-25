namespace LLMCostControl.Admin.App.Auth;

/// <summary>
/// EntraID app role names used for authorization in the admin app (§12.2).
/// These roles are assigned to users in EntraID and appear in the
/// <c>roles</c> claim of the OIDC token.
/// </summary>
public static class AppRoles
{
    /// <summary>Full admin access: create/manage groups, budgets, membership, overrides.</summary>
    public const string Admin = "CostTracker.Admin";

    /// <summary>Read-only access: view groups, budgets, pricing, usage events.</summary>
    public const string ReadOnly = "CostTracker.ReadOnly";
}
