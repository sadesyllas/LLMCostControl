using System.Collections.Generic;
using System.Linq;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LLMCostControl.Infrastructure.Pricing;

/// <summary>
/// Result of a pricing file import operation.
/// </summary>
public sealed class PricingFileImportResult
{
    /// <summary>True when the file was valid and all entries were persisted.</summary>
    public bool Success { get; init; }

    /// <summary>The number of model entries imported (0 when invalid).</summary>
    public int ImportedCount { get; init; }

    /// <summary>The model names that were updated (empty when invalid).</summary>
    public IReadOnlyList<string> UpdatedModels { get; init; } = [];

    /// <summary>Validation errors (empty when valid).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Creates a successful import result.</summary>
    public static PricingFileImportResult Ok(int count, IReadOnlyList<string> models) => new()
    {
        Success = true,
        ImportedCount = count,
        UpdatedModels = models,
    };

    /// <summary>Creates a failed import result with the given errors.</summary>
    public static PricingFileImportResult Fail(IReadOnlyList<string> errors) => new()
    {
        Success = false,
        Errors = errors,
    };
}

/// <summary>
/// Imports a canonical pricing file (§8.5) received from the localhost import
/// endpoint (§8.3). Validates the file using the shared <see cref="PricingFileValidator"/>
/// (M5), then persists all entries to PostgreSQL using the same write path as the
/// refresh job (M7): per-provider replace within a single transaction. After
/// persisting, publishes <see cref="PricingUpdatedEvent"/>s so <c>PricingGrain</c>
/// activations pick up the new values. Atomic reject — no partial imports.
/// </summary>
public sealed class PricingFileImporter
{
    private readonly IDbContextFactory<CostTrackerDbContext> _dbContextFactory;
    private readonly IPricingUpdatePublisher _publisher;
    private readonly ILogger<PricingFileImporter> _logger;

    /// <summary>
    /// Creates the importer with the given DbContext factory, event publisher,
    /// and logger.
    /// </summary>
    public PricingFileImporter(
        IDbContextFactory<CostTrackerDbContext> dbContextFactory,
        IPricingUpdatePublisher publisher,
        ILogger<PricingFileImporter> logger)
    {
        _dbContextFactory = dbContextFactory;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Validates, persists, and publishes a pricing file. The file is rejected
    /// atomically if any entry is invalid — no partial writes occur.
    /// </summary>
    /// <param name="json">The raw JSON content of the pricing file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The import result indicating success or listing errors.</returns>
    public async Task<PricingFileImportResult> ImportAsync(string json, CancellationToken ct = default)
    {
        var parsed = PricingFileValidator.Parse(json);

        if (!parsed.IsValid)
        {
            _logger.LogWarning("Pricing file import rejected: {ErrorCount} validation error(s).", parsed.Errors.Count);
            return PricingFileImportResult.Fail(parsed.Errors);
        }

        var byProvider = parsed.Entries
            .GroupBy(e => e.Provider)
            .ToList();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var group in byProvider)
            {
                await db.ModelPricing
                    .Where(p => p.Provider == group.Key)
                    .ExecuteDeleteAsync(ct);

                await db.ModelPricing.AddRangeAsync(group.ToList(), ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Error persisting pricing file import.");
            throw;
        }

        var allModels = parsed.Entries.Select(e => e.Model).ToList();

        foreach (var group in byProvider)
        {
            var modelNames = group.Select(e => e.Model).ToList();
            await _publisher.PublishAsync(group.Key, modelNames, ct);
        }

        _logger.LogInformation(
            "Imported {Count} model(s) across {ProviderCount} provider(s).",
            parsed.Entries.Count,
            byProvider.Count);

        return PricingFileImportResult.Ok(parsed.Entries.Count, allModels);
    }
}
