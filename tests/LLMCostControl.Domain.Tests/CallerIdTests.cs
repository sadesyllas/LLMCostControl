using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Tests;

public class CallerIdTests
{
    [Fact]
    public void From_trims_whitespace()
    {
        var id = CallerId.From("  alice@example.com  ");

        id.Value.Should().Be("alice@example.com");
    }

    [Fact]
    public void From_rejects_empty_string()
    {
        var act = () => CallerId.From("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_rejects_whitespace_only()
    {
        var act = () => CallerId.From("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_equality_compares_value()
    {
        var a = CallerId.From("alice@example.com");
        var b = CallerId.From("alice@example.com");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ToString_returns_value()
    {
        var id = CallerId.From("alice@example.com");

        id.ToString().Should().Be("alice@example.com");
    }
}
