using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;

namespace LLMCostControl.Infrastructure.Tests;

public class AdapterHappyPathTests
{
    private static readonly string OpenAIFixture = """
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
              "prices": { "input": 2.50, "output": 10.00, "cacheRead": 1.25, "cacheWrite": null }
            },
            {
              "model": "gpt-4o-mini",
              "fetchedAt": "2026-06-24T12:00:00Z",
              "prices": { "input": 0.15, "output": 0.60, "cacheRead": 0.075, "cacheWrite": null }
            }
          ]
        }
      ]
    }
    """;

    private static readonly string AnthropicFixture = """
    {
      "generatedAt": "2026-06-24T12:00:00Z",
      "currency": "USD",
      "unit": "per-1M-tokens",
      "providers": [
        {
          "provider": "anthropic",
          "models": [
            {
              "model": "claude-3-5-sonnet",
              "fetchedAt": "2026-06-24T12:00:00Z",
              "prices": { "input": 3.00, "output": 15.00, "cacheRead": 0.30, "cacheWrite": 3.75 }
            }
          ]
        }
      ]
    }
    """;

    private static readonly string GoogleFixture = """
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
              "prices": { "input": 1.25, "output": 5.00, "cacheRead": null, "cacheWrite": null }
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public async Task OpenAI_adapter_fetches_and_parses_correctly()
    {
        using var handler = new StubHttpMessageHandler(OpenAIFixture);
        using var client = new HttpClient(handler);
        var adapter = new OpenAIPricingAdapter(client, "http://test/openai", null!);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(2);
        var gpt4o = results.Single(r => r.Model == "gpt-4o");
        gpt4o.Provider.Should().Be(Provider.OpenAI);
        gpt4o.Prices.Input.Should().Be(2.5m);
        gpt4o.Prices.Output.Should().Be(10m);
        gpt4o.Prices.CacheRead.Should().Be(1.25m);
        gpt4o.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task Anthropic_adapter_fetches_and_parses_correctly()
    {
        using var handler = new StubHttpMessageHandler(AnthropicFixture);
        using var client = new HttpClient(handler);
        var adapter = new AnthropicPricingAdapter(client, "http://test/anthropic", null!);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var claude = results.Single();
        claude.Provider.Should().Be(Provider.Anthropic);
        claude.Model.Should().Be("claude-3-5-sonnet");
        claude.Prices.Input.Should().Be(3m);
        claude.Prices.Output.Should().Be(15m);
        claude.Prices.CacheRead.Should().Be(0.3m);
        claude.Prices.CacheWrite.Should().Be(3.75m);
        claude.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task Google_adapter_fetches_and_parses_correctly()
    {
        using var handler = new StubHttpMessageHandler(GoogleFixture);
        using var client = new HttpClient(handler);
        var adapter = new GooglePricingAdapter(client, "http://test/google", null!);

        var results = await adapter.FetchAsync();

        results.Should().HaveCount(1);
        var gemini = results.Single();
        gemini.Provider.Should().Be(Provider.Google);
        gemini.Model.Should().Be("gemini-1.5-pro");
        gemini.Prices.Input.Should().Be(1.25m);
        gemini.Prices.Output.Should().Be(5m);
        gemini.Prices.CacheRead.Should().BeNull();
        gemini.IsStale.Should().BeFalse();
    }
}
