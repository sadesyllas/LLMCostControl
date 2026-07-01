using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Infrastructure.Tests;

public class PricingFileValidatorTests
{
    private static readonly string ValidJson = """
    {
      "generatedAt": "2026-06-24T12:00:00Z",
      "currency": "USD",
      "unit": "per-1M-tokens",
      "providers": [
        {
          "provider": "openai",
          "models": [
            {
              "model": "gpt-4o",
              "fetchedAt": "2026-06-24T12:00:00Z",
              "prices": {
                "input": 2.50,
                "output": 10.00,
                "cacheRead": 1.25,
                "cacheWrite": null
              }
            }
          ]
        },
        {
          "provider": "anthropic",
          "models": [
            {
              "model": "claude-3-5-sonnet",
              "fetchedAt": "2026-06-24T12:00:00Z",
              "prices": {
                "input": 3.00,
                "output": 15.00,
                "cacheRead": 0.30,
                "cacheWrite": 3.75
              }
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Valid_file_parses_successfully()
    {
        var result = PricingFileValidator.Parse(ValidJson);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Currency.Should().Be("USD");
        result.Unit.Should().Be("per-1M-tokens");
        result.Entries.Should().HaveCount(2);

        var gpt4o = result.Entries.Single(e => e.Model == "gpt-4o");
        gpt4o.Provider.Should().Be(Provider.OpenAI);
        gpt4o.Prices.Input.Should().Be(2.5m);
        gpt4o.Prices.Output.Should().Be(10m);
        gpt4o.Prices.CacheRead.Should().Be(1.25m);
        gpt4o.Prices.CacheWrite.Should().BeNull();
        gpt4o.Currency.Should().Be("USD");
        gpt4o.Unit.Should().Be("per-1M-tokens");

        var claude = result.Entries.Single(e => e.Model == "claude-3-5-sonnet");
        claude.Provider.Should().Be(Provider.Anthropic);
        claude.Prices.CacheWrite.Should().Be(3.75m);
    }

    [Fact]
    public void CacheRead_null_is_accepted_when_model_has_no_cache()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "google",
              "models": [
                {
                  "model": "gemini-1.5-pro",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 1.25,
                    "output": 5.00,
                    "cacheRead": null,
                    "cacheWrite": null
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeTrue();
        result.Entries.Should().HaveCount(1);
        result.Entries[0].Prices.CacheRead.Should().BeNull();
    }

    [Fact]
    public void Missing_input_field_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "output": 10.00,
                    "cacheRead": 1.25
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Missing_output_field_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.50,
                    "cacheRead": 1.25
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Missing_cacheRead_field_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.50,
                    "output": 10.00
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "mistral",
              "models": [
                {
                  "model": "mistral-large",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.00,
                    "output": 6.00,
                    "cacheRead": null
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Contains("mistral"));
    }

    [Fact]
    public void Negative_input_price_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": -1.0,
                    "output": 10.00,
                    "cacheRead": 1.25
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Contains("negative"));
    }

    [Fact]
    public void Malformed_JSON_is_rejected()
    {
        var json = "{ this is not valid json";

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Empty_providers_list_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": []
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Contains("provider"));
    }

    [Fact]
    public void Missing_model_name_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.50,
                    "output": 10.00,
                    "cacheRead": 1.25
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Contains("Model name"));
    }

    [Fact]
    public void Missing_file_currency_is_rejected()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.50,
                    "output": 10.00,
                    "cacheRead": 1.25
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Contains("currency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Partial_failure_rejects_entire_file()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "openai",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.50,
                    "output": 10.00,
                    "cacheRead": 1.25
                  }
                },
                {
                  "model": "gpt-4o-mini",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": -5,
                    "output": 0.60,
                    "cacheRead": 0.075
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validator_accepts_azure_foundry_and_vertex_ai_providers()
    {
        var json = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            {
              "provider": "azure-foundry",
              "models": [
                {
                  "model": "gpt-4o",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 2.50,
                    "output": 10.00,
                    "cacheRead": 1.25,
                    "cacheWrite": null
                  }
                }
              ]
            },
            {
              "provider": "vertex-ai",
              "models": [
                {
                  "model": "gemini-1.5-pro",
                  "fetchedAt": "2026-06-24T12:00:00Z",
                  "prices": {
                    "input": 1.25,
                    "output": 5.00,
                    "cacheRead": null,
                    "cacheWrite": null
                  }
                }
              ]
            }
          ]
        }
        """;

        var result = PricingFileValidator.Parse(json);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Entries.Should().HaveCount(2);

        var azureGpt = result.Entries.Single(e => e.Provider == Provider.AzureFoundry);
        azureGpt.Model.Should().Be("gpt-4o");

        var vertexGemini = result.Entries.Single(e => e.Provider == Provider.VertexAI);
        vertexGemini.Model.Should().Be("gemini-1.5-pro");
    }
}
