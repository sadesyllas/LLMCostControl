using LLMCostControl.Domain.Pricing;
using Microsoft.Extensions.Logging;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Validates and imports a canonical pricing file (§8.3, §8.5). Shared between
/// the Tracker API localhost import endpoint (M14) and the Admin app upload path
/// (M18). Persists via <see cref="IPricingStoreWriter"/> and publishes
/// pricing-updated stream events so pricing grains refresh immediately.
/// </summary>
public sealed class PricingImportService
{
    private readonly IPricingStoreWriter _writer;
    private readonly IPricingUpdatePublisher _publisher;
    private readonly ILogger<PricingImportService> _logger;

    /// <summary>Creates the import service.</summary>
    public PricingImportService(
        IPricingStoreWriter writer,
        IPricingUpdatePublisher publisher,
        ILogger<PricingImportService> logger)
    {
        _writer = writer;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Parses and validates the supplied JSON pricing file, atomically persists
    /// all entries, and publishes a pricing-updated event per provider. Returns a
    /// failure result (with errors listed) on any validation issue — no partial
    /// imports are performed.
    /// </summary>
    /// <param name="json">The raw JSON pricing file content (§8.5 schema).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PricingImportResult> ImportAsync(string json, CancellationToken ct = default)
    {
        var parseResult = PricingFileValidator.Parse(json);
        if (!parseResult.IsValid)
        {
            _logger.LogWarning("Pricing file import rejected: {ErrorCount} error(s).", parseResult.Errors.Count);
            return PricingImportResult.Failure(parseResult.Errors);
        }

        var totalCount = 0;

        foreach (var group in parseResult.Entries.GroupBy(e => e.Provider))
        {
            var entries = (IReadOnlyCollection<ModelPricing>)group.ToList();
            await _writer.ReplaceProviderAsync(group.Key, entries, ct);

            var modelNames = group.Select(e => e.Model).ToList();
            await _publisher.PublishAsync(group.Key, modelNames, ct);

            totalCount += entries.Count;
            _logger.LogInformation(
                "Imported {Count} model(s) for provider {Provider}.",
                entries.Count,
                group.Key);
        }

        return PricingImportResult.Success(totalCount);
    }
}
