using LLMCostControl.Domain.Pricing;

namespace LLMCostControl.Domain.Tests;

public class ProviderResolverTests
{
    [Theory]
    [InlineData("openai", Provider.OpenAI)]
    [InlineData("OpenAI", Provider.OpenAI)]
    [InlineData("ANTHROPIC", Provider.Anthropic)]
    [InlineData("google", Provider.Google)]
    public void TryParseProvider_parses_known_names_case_insensitively(string value, Provider expected)
    {
        ProviderResolver.TryParseProvider(value, out var provider).Should().BeTrue();
        provider.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("foobar")]
    public void TryParseProvider_rejects_unknown_or_empty(string? value)
    {
        ProviderResolver.TryParseProvider(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Key_builds_lowercase_composite()
    {
        ProviderResolver.Key(Provider.OpenAI, "gpt-4o").Should().Be("openai:gpt-4o");
        ProviderResolver.Key(Provider.Anthropic, "claude-3-5-sonnet").Should().Be("anthropic:claude-3-5-sonnet");
    }

    [Fact]
    public void TryParseKey_round_trips()
    {
        ProviderResolver.TryParseKey("openai:gpt-4o", out var provider, out var model).Should().BeTrue();
        provider.Should().Be(Provider.OpenAI);
        model.Should().Be("gpt-4o");
    }

    [Fact]
    public void TryParseKey_preserves_colons_in_model_name()
    {
        // OpenAI fine-tune ids contain colons; only the first ':' delimits the provider.
        ProviderResolver.TryParseKey("openai:ft:gpt-4o:org:abc", out var provider, out var model).Should().BeTrue();
        provider.Should().Be(Provider.OpenAI);
        model.Should().Be("ft:gpt-4o:org:abc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-colon")]
    [InlineData(":model")]
    [InlineData("openai:")]
    [InlineData("unknownprovider:model")]
    public void TryParseKey_rejects_malformed_keys(string key)
    {
        ProviderResolver.TryParseKey(key, out _, out _).Should().BeFalse();
    }
}

public class ProviderInferenceMapTests
{
    private static ProviderInferenceMap BuildDefaultMap() => new(new Dictionary<string, string>
    {
        ["claude"] = "anthropic",
        ["gpt"] = "openai",
        ["chatgpt"] = "openai",
        ["o1"] = "openai",
        ["o3"] = "openai",
        ["gemini"] = "google",
    });

    [Theory]
    [InlineData("gpt-4o", Provider.OpenAI)]
    [InlineData("chatgpt-4o-latest", Provider.OpenAI)]
    [InlineData("o3-mini", Provider.OpenAI)]
    [InlineData("claude-3-5-sonnet", Provider.Anthropic)]
    [InlineData("gemini-1.5-pro", Provider.Google)]
    public void TryInfer_matches_known_prefixes(string model, Provider expected)
    {
        BuildDefaultMap().TryInfer(model, out var provider).Should().BeTrue();
        provider.Should().Be(expected);
    }

    [Theory]
    [InlineData("mistral-large")]
    [InlineData("llama-3")]
    [InlineData("")]
    public void TryInfer_returns_false_for_unknown_models(string model)
    {
        BuildDefaultMap().TryInfer(model, out _).Should().BeFalse();
    }

    [Fact]
    public void TryInfer_prefers_the_longest_matching_prefix()
    {
        var map = new ProviderInferenceMap(new Dictionary<string, string>
        {
            ["gpt"] = "openai",
            ["gpt-legacy"] = "google", // contrived: a longer, more specific prefix wins
        });

        map.TryInfer("gpt-legacy-001", out var provider).Should().BeTrue();
        provider.Should().Be(Provider.Google);
    }

    [Fact]
    public void Constructor_ignores_unparseable_entries()
    {
        var map = new ProviderInferenceMap(new Dictionary<string, string>
        {
            ["good"] = "openai",
            ["bad"] = "not-a-provider",
        });

        map.Entries.Should().ContainSingle().Which.Provider.Should().Be(Provider.OpenAI);
    }

    [Fact]
    public void TryResolve_prefers_explicit_provider_over_inference()
    {
        // Model would infer OpenAI, but an explicit (recognised) provider wins.
        BuildDefaultMap().TryResolve("google", "gpt-4o", out var provider).Should().BeTrue();
        provider.Should().Be(Provider.Google);
    }

    [Fact]
    public void TryResolve_falls_back_to_inference_when_provider_absent_or_unknown()
    {
        var map = BuildDefaultMap();

        map.TryResolve(null, "claude-3-haiku", out var inferred).Should().BeTrue();
        inferred.Should().Be(Provider.Anthropic);

        // An unrecognised explicit provider is treated as "not given" → inferred.
        map.TryResolve("bogus", "gemini-2.0", out var inferred2).Should().BeTrue();
        inferred2.Should().Be(Provider.Google);
    }

    [Fact]
    public void TryResolve_returns_false_when_neither_explicit_nor_inferable()
    {
        BuildDefaultMap().TryResolve(null, "mistral-large", out _).Should().BeFalse();
    }
}
