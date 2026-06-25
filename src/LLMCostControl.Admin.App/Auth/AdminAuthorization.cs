namespace LLMCostControl.Admin.App.Auth;

/// <summary>
/// Role and authorization-policy names for the admin app (§12.2). Roles are
/// EntraID (Azure AD) app roles surfaced in the access/ID token's <c>roles</c>
/// claim; assignment is managed in EntraID, not in the tracker's database.
/// </summary>
public static class AdminAuthorization
{
    /// <summary>EntraID app role granting full administrative (write) commands.</summary>
    public const string AdminRole = "CostTracker.Admin";

    /// <summary>EntraID app role granting read-only access.</summary>
    public const string ReadOnlyRole = "CostTracker.ReadOnly";

    /// <summary>Policy gating administrative (write) commands — the Admin role only.</summary>
    public const string AdminPolicy = "CostTracker.AdminPolicy";

    /// <summary>Policy gating read-only access — the Admin or ReadOnly role.</summary>
    public const string ReadOnlyPolicy = "CostTracker.ReadOnlyPolicy";
}
