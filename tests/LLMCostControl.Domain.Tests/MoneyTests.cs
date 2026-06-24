using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Constructor_normalizes_currency_to_uppercase()
    {
        var money = new Money(10.5m, "usd");

        money.Currency.Should().Be("USD");
        money.Amount.Should().Be(10.5m);
    }

    [Fact]
    public void Constructor_rejects_empty_currency()
    {
        var act = () => new Money(10m, "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_non_3_letter_currency()
    {
        var act = () => new Money(10m, "DOLLAR");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_same_currency_sums_amounts()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5.5m, "USD");

        var result = a.Add(b);

        result.Should().Be(new Money(15.5m, "USD"));
    }

    [Fact]
    public void Subtract_same_currency_subtracts_amounts()
    {
        var a = new Money(10m, "USD");
        var b = new Money(3m, "USD");

        var result = a.Subtract(b);

        result.Should().Be(new Money(7m, "USD"));
    }

    [Fact]
    public void Add_different_currencies_throws()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "EUR");

        var act = () => a.Add(b);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*USD*EUR*");
    }

    [Fact]
    public void Subtract_different_currencies_throws()
    {
        var a = new Money(10m, "USD");
        var b = new Money(5m, "EUR");

        var act = () => a.Subtract(b);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Multiply_scales_amount()
    {
        var money = new Money(2.5m, "USD");

        var result = money.Multiply(4m);

        result.Should().Be(new Money(10m, "USD"));
    }

    [Fact]
    public void IsNegative_true_for_negative_amount()
    {
        var money = new Money(-1m, "USD");

        money.IsNegative.Should().BeTrue();
    }

    [Fact]
    public void IsZero_true_for_zero_amount()
    {
        var money = Money.Zero("USD");

        money.IsZero.Should().BeTrue();
        money.IsNegative.Should().BeFalse();
    }

    [Fact]
    public void Comparison_operators_work_for_same_currency()
    {
        var small = new Money(5m, "USD");
        var big = new Money(10m, "USD");
        var alsoSmall = new Money(5m, "USD");

        (small < big).Should().BeTrue();
        (big > small).Should().BeTrue();
        (small <= alsoSmall).Should().BeTrue();
        (big >= alsoSmall).Should().BeTrue();
    }

    [Fact]
    public void Record_equality_compares_amount_and_currency()
    {
        var a = new Money(10m, "USD");
        var b = new Money(10m, "USD");
        var c = new Money(10m, "EUR");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
