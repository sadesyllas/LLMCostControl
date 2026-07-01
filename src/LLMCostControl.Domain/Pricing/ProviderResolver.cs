namespace LLMCostControl.Domain.Pricing;

/// <summary>
/// Pure helpers for resolving a <see cref="Provider"/> and for building/parsing
/// the composite <c>"{provider}:{model}"</c> key used to identify pricing
/// per (provider, model) on the capture path (§8.5, §8.6).
/// <para>
/// This type contains <b>no</b> provider/model knowledge of its own. Prefix-based
/// inference is supplied externally via <see cref="ProviderInferenceMap"/>, whose
/// data comes from configuration (<c>Pricing:ProviderInference</c>), not from
/// hard-coded values.
/// </para>
/// </summary>
public static class ProviderResolver
{
    /// <summary>
    /// Parses a provider string (e.g. <c>"openai"</c>) to a <see cref="Provider"/>,
    /// case-insensitively. Returns false for null/empty/unrecognised values.
    /// </summary>
    public static bool TryParseProvider(string? value, out Provider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized == "azure-foundry" || normalized == "azurefoundry")
        {
            provider = Provider.AzureFoundry;
            return true;
        }
        if (normalized == "vertex-ai" || normalized == "vertexai")
        {
            provider = Provider.VertexAI;
            return true;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out provider) && Enum.IsDefined(provider);
    }

    /// <summary>The canonical lowercase string form of a provider (e.g. <c>"openai"</c>).</summary>
    public static string ToCanonicalString(Provider provider) => provider switch
    {
        Provider.AzureFoundry => "azure-foundry",
        Provider.VertexAI => "vertex-ai",
        _ => provider.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// Builds the composite key <c>"{provider}:{model}"</c> used to key the
    /// <c>PricingGrain</c> and the per-silo pricing cache.
    /// </summary>
    public static string Key(Provider provider, string model) => $"{ToCanonicalString(provider)}:{model}";

    /// <summary>
    /// Splits a composite <c>"{provider}:{model}"</c> key back into its parts.
    /// Splits on the first <c>':'</c> only, so model names that themselves
    /// contain colons (e.g. OpenAI fine-tune ids) are preserved. Returns false
    /// when the key is malformed or the provider segment is unrecognised.
    /// </summary>
    public static bool TryParseKey(string key, out Provider provider, out string model)
    {
        provider = default;
        model = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var idx = key.IndexOf(':');
        if (idx <= 0 || idx == key.Length - 1)
        {
            return false;
        }

        if (!TryParseProvider(key[..idx], out provider))
        {
            return false;
        }

        model = key[(idx + 1)..];
        return true;
    }
}
