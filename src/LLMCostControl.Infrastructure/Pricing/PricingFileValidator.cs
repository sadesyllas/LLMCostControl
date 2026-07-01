using System.Text.Json;
using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Infrastructure.Pricing;


/// <summary>
/// Validates and parses a canonical pricing file (§8.5). Deserialises JSON,
/// then checks every model entry for required fields, consistent
/// currency/unit, known providers, and non-negative prices. On any error, the
/// entire file is rejected — no partial imports.
/// </summary>
public static class PricingFileValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai", "anthropic", "google",
    };

    /// <summary>
    /// Parses and validates a pricing file from a JSON string. Returns a
    /// <see cref="PricingFileParseResult"/> indicating success or listing all
    /// validation errors.
    /// </summary>
    public static PricingFileParseResult Parse(string json)
    {
        PricingFile? file;
        try
        {
            file = JsonSerializer.Deserialize<PricingFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return PricingFileParseResult.Failure([$"Invalid JSON: {ex.Message}"]);
        }

        if (file is null)
        {
            return PricingFileParseResult.Failure(["File deserialised to null."]);
        }

        var errors = new List<string>();
        var currency = file.Currency;
        var unit = file.Unit;

        if (string.IsNullOrWhiteSpace(currency))
        {
            errors.Add("File-level 'currency' is required.");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            errors.Add("File-level 'unit' is required.");
        }

        if (file.Providers.Count == 0)
        {
            errors.Add("At least one provider entry is required.");
        }

        var entries = new List<ModelPricing>();

        foreach (var providerEntry in file.Providers)
        {
            var providerName = providerEntry.Provider;

            if (string.IsNullOrWhiteSpace(providerName))
            {
                errors.Add("Provider name is required.");
                continue;
            }

            if (!KnownProviders.Contains(providerName))
            {
                errors.Add($"Unknown provider '{providerName}'. Allowed: openai, anthropic, google.");
                continue;
            }

            if (!Enum.TryParse<Provider>(providerName, ignoreCase: true, out var provider))
            {
                errors.Add($"Provider '{providerName}' could not be mapped to the Provider enum.");
                continue;
            }

            if (providerEntry.Models.Count == 0)
            {
                errors.Add($"Provider '{providerName}' has no model entries.");
                continue;
            }

            foreach (var modelEntry in providerEntry.Models)
            {
                var modelErrors = ValidateModelEntry(modelEntry, currency, unit, provider);
                errors.AddRange(modelErrors.Errors);

                if (modelErrors.IsValid)
                {
                    entries.Add(modelErrors.Pricing!);
                }
            }
        }

        if (errors.Count > 0)
        {
            return PricingFileParseResult.Failure(errors);
        }

        return PricingFileParseResult.Success(entries, currency, unit);
    }

    private static (bool IsValid, ModelPricing? Pricing, List<string> Errors) ValidateModelEntry(
        PricingFileModel modelEntry,
        string fileCurrency,
        string fileUnit,
        Provider provider)
    {
        var errors = new List<string>();
        var modelLabel = $"{provider}/{modelEntry.Model ?? "(empty)"}";

        if (string.IsNullOrWhiteSpace(modelEntry.Model))
        {
            errors.Add($"[{modelLabel}] Model name is required.");
            return (false, null, errors);
        }

        var prices = modelEntry.Prices;

        if (prices.Input < 0m)
        {
            errors.Add($"[{modelLabel}] 'input' price cannot be negative.");
        }

        if (prices.Output < 0m)
        {
            errors.Add($"[{modelLabel}] 'output' price cannot be negative.");
        }

        if (prices.CacheRead is < 0m)
        {
            errors.Add($"[{modelLabel}] 'cacheRead' price cannot be negative.");
        }

        if (prices.CacheWrite is < 0m)
        {
            errors.Add($"[{modelLabel}] 'cacheWrite' price cannot be negative.");
        }

        if (errors.Count > 0)
        {
            return (false, null, errors);
        }

        var tokenPrices = TokenPrices.Create(
            prices.Input,
            prices.Output,
            prices.CacheRead,
            prices.CacheWrite);

        var pricing = ModelPricing.Create(
            provider,
            modelEntry.Model,
            tokenPrices,
            currency: fileCurrency,
            unit: fileUnit,
            fetchedAt: modelEntry.FetchedAt);

        return (true, pricing, errors);
    }
}
