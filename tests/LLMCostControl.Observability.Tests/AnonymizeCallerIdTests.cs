using LLMCostControl.Observability;

namespace LLMCostControl.Observability.Tests;

/// <summary>
/// Unit tests for <see cref="ObservabilityExtensions.AnonymizeCallerId"/>, the
/// HMAC-SHA256 pseudonymisation applied to <c>caller_id</c> before it is recorded
/// in telemetry (§10.2). Each test sets a known pepper first.
/// </summary>
public class AnonymizeCallerIdTests
{
    [Fact]
    public void Is_deterministic_and_returns_lowercase_sha256_hex()
    {
        ObservabilityExtensions.TelemetryPepper = "unit-test-pepper";

        var first = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");
        var second = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");

        first.Should().Be(second, "the same input + pepper must hash identically (for metric correlation).");
        first.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Normalises_case_and_surrounding_whitespace()
    {
        ObservabilityExtensions.TelemetryPepper = "unit-test-pepper";

        var canonical = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");

        ObservabilityExtensions.AnonymizeCallerId("Alice@Example.com").Should().Be(canonical);
        ObservabilityExtensions.AnonymizeCallerId("  alice@example.com  ").Should().Be(canonical);
    }

    [Fact]
    public void Produces_different_hashes_for_different_callers()
    {
        ObservabilityExtensions.TelemetryPepper = "unit-test-pepper";

        var a = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");
        var b = ObservabilityExtensions.AnonymizeCallerId("bob@example.com");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Changes_with_the_pepper()
    {
        ObservabilityExtensions.TelemetryPepper = "pepper-one";
        var withFirst = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");

        ObservabilityExtensions.TelemetryPepper = "pepper-two";
        var withSecond = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");

        withSecond.Should().NotBe(withFirst, "a different secret pepper must yield a different hash.");
    }

    [Fact]
    public void Does_not_leak_the_raw_caller_id()
    {
        ObservabilityExtensions.TelemetryPepper = "unit-test-pepper";

        var hash = ObservabilityExtensions.AnonymizeCallerId("alice@example.com");

        hash.Should().NotContain("alice").And.NotContain("@").And.NotContain("example");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_empty_for_null_or_whitespace(string? callerId)
    {
        ObservabilityExtensions.TelemetryPepper = "unit-test-pepper";

        ObservabilityExtensions.AnonymizeCallerId(callerId!).Should().BeEmpty();
    }
}
