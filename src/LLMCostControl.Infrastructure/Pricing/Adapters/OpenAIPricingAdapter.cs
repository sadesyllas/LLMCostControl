using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Pricing adapter for OpenAI. Fetches current pricing from a configurable URL
/// that serves the canonical pricing file format (§8.5). On failure, falls back
/// to persisted DB values with a staleness signal.
/// </summary>
public sealed class OpenAIPricingAdapter : PricingAdapterBase
{
    private readonly HttpClient _httpClient;
    private readonly string _sourceUrl;

    /// <summary>The provider this adapter handles.</summary>
    public override Provider Provider => Provider.OpenAI;

    /// <summary>
    /// Creates the adapter.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for live fetching.</param>
    /// <param name="sourceUrl">The URL serving the canonical pricing file for OpenAI.</param>
    /// <param name="repository">The repository for persisted fallback reads.</param>
    public OpenAIPricingAdapter(HttpClient httpClient, string sourceUrl, ModelPricingRepository repository)
        : base(repository)
    {
        _httpClient = httpClient;
        _sourceUrl = sourceUrl;
    }

    /// <summary>
    /// Fetches live pricing from the configured source URL and parses the
    /// canonical pricing file, filtering to OpenAI entries only.
    /// </summary>
    protected override async Task<IReadOnlyCollection<ModelPricing>> FetchLiveAsync(CancellationToken ct)
    {
        var json = await _httpClient.GetStringAsync(_sourceUrl, ct);
        var result = PricingFileValidator.Parse(json);

        if (!result.IsValid)
        {
            throw new InvalidOperationException(
                $"OpenAI pricing source returned an invalid file: {string.Join("; ", result.Errors)}");
        }

        return result.Entries
            .Where(e => e.Provider == Provider.OpenAI)
            .ToList();
    }
}
