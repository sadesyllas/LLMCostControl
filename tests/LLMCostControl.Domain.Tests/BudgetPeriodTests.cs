using LLMCostControl.Domain.Common;

namespace LLMCostControl.Domain.Tests;

public class BudgetPeriodTests
{
    [Fact]
    public void Constructor_accepts_valid_month()
    {
        var period = new BudgetPeriod(2026, 6);

        period.Year.Should().Be(2026);
        period.Month.Should().Be(6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Constructor_rejects_invalid_month(int month)
    {
        var act = () => new BudgetPeriod(2026, month);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromDate_extracts_year_and_month()
    {
        var date = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);

        var period = BudgetPeriod.FromDate(date);

        period.Should().Be(new BudgetPeriod(2026, 3));
    }

    [Fact]
    public void Next_rolls_over_from_december_to_january()
    {
        var period = new BudgetPeriod(2026, 12);

        var next = period.Next();

        next.Should().Be(new BudgetPeriod(2027, 1));
    }

    [Fact]
    public void Next_increments_month_within_same_year()
    {
        var period = new BudgetPeriod(2026, 6);

        var next = period.Next();

        next.Should().Be(new BudgetPeriod(2026, 7));
    }

    [Fact]
    public void Previous_rolls_over_from_january_to_december()
    {
        var period = new BudgetPeriod(2026, 1);

        var prev = period.Previous();

        prev.Should().Be(new BudgetPeriod(2025, 12));
    }

    [Fact]
    public void Contains_true_for_date_in_same_month()
    {
        var period = new BudgetPeriod(2026, 6);
        var date = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        period.Contains(date).Should().BeTrue();
    }

    [Fact]
    public void Contains_false_for_date_in_different_month()
    {
        var period = new BudgetPeriod(2026, 6);
        var date = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        period.Contains(date).Should().BeFalse();
    }

    [Fact]
    public void StartDate_is_first_day_of_month_at_utc_midnight()
    {
        var period = new BudgetPeriod(2026, 6);

        period.StartDate.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void EndDate_is_first_day_of_next_month()
    {
        var period = new BudgetPeriod(2026, 6);

        period.EndDate.Should().Be(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToString_formats_as_year_dash_month()
    {
        var period = new BudgetPeriod(2026, 6);

        period.ToString().Should().Be("2026-06");
    }
}
