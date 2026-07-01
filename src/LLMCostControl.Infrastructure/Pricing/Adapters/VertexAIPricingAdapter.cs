using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Pricing adapter for Vertex AI. Fetches current pricing from a configurable
/// URL that serves the canonical pricing file format (§8.5). On failure, falls
/// back to persisted DB values with a staleness signal.
/// </summary>
public sealed class VertexAIPricingAdapter : PricingAdapterBase
{
    private readonly HttpClient _httpClient;
    private readonly string _sourceUrl;

    /// <summary>The provider this adapter handles.</summary>
    public override Provider Provider => Provider.VertexAI;

    /// <summary>
    /// Creates the adapter.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for live fetching.</param>
    /// <param name="sourceUrl">The URL serving the canonical pricing file for Vertex AI.</param>
    /// <param name="repository">The repository for persisted fallback reads.</param>
    public VertexAIPricingAdapter(HttpClient httpClient, string sourceUrl, ModelPricingRepository repository)
        : base(repository)
    {
        _httpClient = httpClient;
        _sourceUrl = sourceUrl;
    }

    /// <summary>
    /// Fetches live pricing from the configured source URL and parses the
    /// canonical pricing file, filtering to Vertex AI entries only.
    /// </summary>
    protected override Task<IReadOnlyCollection<ModelPricing>> FetchLiveAsync(CancellationToken ct)
        => FetchAndFilterLiveAsync(_httpClient, _sourceUrl, ct);
}
