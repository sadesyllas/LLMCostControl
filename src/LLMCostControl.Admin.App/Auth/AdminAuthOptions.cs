namespace LLMCostControl.Admin.App.Auth;

/// <summary>
/// Configuration options for EntraID (Azure AD) OIDC authentication (§12.2).
/// Bound from the <c>EntraId</c> configuration section.
/// </summary>
public sealed class AdminAuthOptions
{
    /// <summary>Whether EntraID auth is enabled. When false (dev mode), the app
    /// runs without authentication.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>The EntraID instance URL (e.g. <c>https://login.microsoftonline.com/</c>).</summary>
    public string Instance { get; init; } = "https://login.microsoftonline.com/";

    /// <summary>The EntraID tenant id (GUID or <c>common</c>).</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>The EntraID app registration client id.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>The EntraID app registration client secret (for confidential clients).</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>The OIDC authority URL derived from <see cref="Instance"/> and <see cref="TenantId"/>.</summary>
    public string Authority => $"{Instance}{TenantId}/v2.0";
}
