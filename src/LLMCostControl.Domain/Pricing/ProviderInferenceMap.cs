namespace LLMCostControl.Domain.Pricing;

/// <summary>
/// Maps model-name prefixes to their default <see cref="Provider"/>, used when a
/// check/capture call does not specify the provider explicitly (§6.2, §8.5).
/// <para>
/// The map is <b>data-driven</b>: its entries are supplied by configuration
/// (the <c>Pricing:ProviderInference</c> section of <c>appsettings.json</c>, a
/// checked-in repository file), never hard-coded here. An empty map simply means
/// no inference is possible — callers without an explicit, known provider are
/// then treated as unknown-model and rejected (fail-closed).
/// </para>
/// </summary>
public sealed class ProviderInferenceMap
{
    private readonly IReadOnlyList<(string Prefix, Provider Provider)> _entries;

    /// <summary>
    /// Builds the map from a raw <c>prefix → provider-name</c> dictionary (as bound
    /// from configuration). Unparseable entries are ignored. Entries are ordered
    /// longest-prefix-first so a more specific prefix wins over a shorter one.
    /// </summary>
    public ProviderInferenceMap(IEnumerable<KeyValuePair<string, string>> rawMap)
    {
        ArgumentNullException.ThrowIfNull(rawMap);

        var list = new List<(string Prefix, Provider Provider)>();
        foreach (var kv in rawMap)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            if (ProviderResolver.TryParseProvider(kv.Value, out var provider))
            {
                list.Add((kv.Key.Trim().ToLowerInvariant(), provider));
            }
        }

        _entries = list
            .OrderByDescending(e => e.Prefix.Length)
            .ToList();
    }

    /// <summary>The parsed inference entries, longest-prefix-first.</summary>
    public IReadOnlyList<(string Prefix, Provider Provider)> Entries => _entries;

    /// <summary>
    /// Infers the provider from a model name by longest-matching prefix.
    /// Returns false when no prefix matches.
    /// </summary>
    public bool TryInfer(string model, out Provider provider)
    {
        provider = default;
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var normalized = model.Trim().ToLowerInvariant();
        foreach (var (prefix, p) in _entries)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                provider = p;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the provider for a call: uses the explicit provider when given and
    /// recognised; otherwise infers from the model-name prefix. Returns false when
    /// neither yields a known provider.
    /// </summary>
    public bool TryResolve(string? explicitProvider, string model, out Provider provider)
        => ProviderResolver.TryParseProvider(explicitProvider, out provider) || TryInfer(model, out provider);
}
