using LLMCostControl.Domain.Pricing;
using Microsoft.Extensions.Logging;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Outcome of a pricing file import (§8.3). Either a success carrying the number
/// of imported models and the affected providers, or a failure carrying the
/// validation errors (in which case nothing was written).
/// </summary>
public sealed class PricingImportResult
{
    /// <summary>True when the file was valid and all entries were persisted.</summary>
    public bool Success { get; private init; }

    /// <summary>Number of model pricing entries imported (zero on failure).</summary>
    public int ImportedModelCount { get; private init; }

    /// <summary>The providers whose pricing was replaced (empty on failure).</summary>
    public IReadOnlyList<Provider> AffectedProviders { get; private init; } = [];

    /// <summary>Validation errors (empty on success).</summary>
    public IReadOnlyList<string> Errors { get; private init; } = [];

    /// <summary>Creates a successful import result.</summary>
    public static PricingImportResult Imported(
        int importedModelCount,
        IReadOnlyList<Provider> affectedProviders) => new()
    {
        Success = true,
        ImportedModelCount = importedModelCount,
        AffectedProviders = affectedProviders,
    };

    /// <summary>Creates a failed import result with the given validation errors.</summary>
    public static PricingImportResult Failed(IReadOnlyList<string> errors) => new()
    {
        Success = false,
        Errors = errors,
    };
}

/// <summary>
/// Imports a canonical pricing file (§8.5) through the same pipeline as a live
/// fetch (§8.3): the shared <see cref="PricingFileValidator"/> (M5) validates the
/// file atomically, then each provider's entries are persisted via the refresh
/// job's write path (<see cref="IPricingWriter"/>, M7) and a
/// <c>pricing-updated</c> event is published per provider so pricing grains pick
/// up the new values (§8.6).
/// <para>
/// Validation is all-or-nothing: an invalid file is rejected before any write,
/// so there are never partial imports. This service lives in Infrastructure so
/// both the tracker's localhost import endpoint (M14) and the admin app's
/// pricing-file upload (M18, §12.3) share the same write code path.
/// </para>
/// </summary>
public sealed class PricingImportService
{
    private readonly IPricingWriter _writer;
    private readonly IPricingUpdatePublisher _publisher;
    private readonly ILogger<PricingImportService> _logger;

    /// <summary>Creates the import service.</summary>
    public PricingImportService(
        IPricingWriter writer,
        IPricingUpdatePublisher publisher,
        ILogger<PricingImportService> logger)
    {
        _writer = writer;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Validates and imports the given pricing file content. On success, replaces
    /// pricing for each provider present in the file and publishes a
    /// <c>pricing-updated</c> event per provider. On any validation error, returns
    /// a failure result and writes nothing.
    /// </summary>
    /// <param name="fileContent">The canonical pricing file content (JSON, §8.5).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PricingImportResult> ImportAsync(string fileContent, CancellationToken ct = default)
    {
        var parsed = PricingFileValidator.Parse(fileContent);

        if (!parsed.IsValid)
        {
            _logger.LogWarning(
                "Pricing file import rejected with {ErrorCount} validation error(s).",
                parsed.Errors.Count);
            return PricingImportResult.Failed(parsed.Errors);
        }

        var affectedProviders = new List<Provider>();

        foreach (var group in parsed.Entries.GroupBy(e => e.Provider))
        {
            var provider = group.Key;
            var entries = group.ToList();

            await _writer.ReplaceProviderPricingAsync(provider, entries, ct);

            var modelNames = entries.Select(e => e.Model).ToList();
            await _publisher.PublishAsync(provider, modelNames, ct);

            affectedProviders.Add(provider);

            _logger.LogInformation(
                "Imported {Count} model(s) for provider {Provider} from pricing file.",
                entries.Count,
                provider);
        }

        return PricingImportResult.Imported(parsed.Entries.Count, affectedProviders);
    }
}
