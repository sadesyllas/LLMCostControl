using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// Test helper that generates an RSA signing key, issues JWTs, and produces a
/// JWKS document for the key. Used by auth tests to simulate a real OAuth
/// issuer.
/// </summary>
public sealed class JwtTestHelper : IDisposable
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly string _kid;

    /// <summary>The issuer used in issued tokens.</summary>
    public string Issuer { get; } = "https://test-gateway.local";

    /// <summary>The audience used in issued tokens.</summary>
    public string Audience { get; } = "llm-cost-control";

    /// <summary>Creates the helper with a fresh RSA key.</summary>
    public JwtTestHelper()
    {
        _rsa = RSA.Create(2048);
        _securityKey = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        _kid = "test-key-1";
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>
    /// Generates a JWT signed with the test key.
    /// </summary>
    /// <param name="issuer">Override issuer (defaults to <see cref="Issuer"/>).</param>
    /// <param name="audience">Override audience (defaults to <see cref="Audience"/>).</param>
    /// <param name="expiresAt">Expiry timestamp; defaults to 1 hour from now.</param>
    /// <param name="notBefore">Not-before timestamp; defaults to now.</param>
    public string GenerateToken(
        string? issuer = null,
        string? audience = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? notBefore = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = expiresAt ?? now.AddHours(1);
        var nbf = notBefore ?? now;

        var claims = new List<Claim>
        {
            new("sub", "gateway-service-principal"),
            new("client_id", "llm-gateway"),
        };

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: nbf.UtcDateTime,
            expires: expiry.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates a JWT signed with a different (invalid) RSA key, simulating
    /// a bad signature.
    /// </summary>
    public string GenerateBadSignatureToken()
    {
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa);
        var otherCreds = new SigningCredentials(otherKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim("sub", "gateway-service-principal") },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: otherCreds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Returns a JWKS JSON document containing the test public key.
    /// </summary>
    public string GetJwksJson()
    {
        var parameters = _rsa.ExportParameters(false);
        var n = Base64UrlEncoder.Encode(parameters.Modulus);
        var e = Base64UrlEncoder.Encode(parameters.Exponent);

        return $$"""
        {
          "keys": [
            {
              "kty": "RSA",
              "use": "sig",
              "kid": "{{_kid}}",
              "alg": "RS256",
              "n": "{{n}}",
              "e": "{{e}}"
            }
          ]
        }
        """;
    }

    /// <summary>Disposes the RSA key.</summary>
    public void Dispose()
    {
        _rsa.Dispose();
    }
}
