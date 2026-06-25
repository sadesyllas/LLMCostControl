namespace LLMCostControl.Tracker.Api.Auth;

/// <summary>
/// Configuration for gateway OAuth/JWKS authentication (§6.1). The tracker
/// validates the gateway's bearer token signature, expiry, issuer, and
/// audience before authorising any call.
/// </summary>
public sealed class GatewayAuthOptions
{
    /// <summary>
    /// The JWKS / public-key endpoint to fetch signing keys from. When empty,
    /// authentication is disabled (development only).
    /// </summary>
    public string JwksEndpoint { get; set; } = string.Empty;

    /// <summary>The expected issuer (<c>iss</c>) claim, or empty to skip.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The expected audience (<c>aud</c>) claim, or empty to skip.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>True when a <see cref="JwksEndpoint"/> is configured.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(JwksEndpoint);
}
