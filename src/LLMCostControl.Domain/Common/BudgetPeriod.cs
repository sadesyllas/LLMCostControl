namespace LLMCostControl.Domain.Common;

/// <summary>
/// Represents a budget period. For iteration 1 the period is a calendar month,
/// identified by year and month.
/// </summary>
public readonly record struct BudgetPeriod
{
    /// <summary>The year component of the period.</summary>
    public int Year { get; }

    /// <summary>The month component (1–12).</summary>
    public int Month { get; }

    /// <summary>
    /// Creates a <see cref="BudgetPeriod"/> from an explicit year and month.
    /// </summary>
    public BudgetPeriod(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentException("Month must be between 1 and 12.", nameof(month));
        }

        Year = year;
        Month = month;
    }

    /// <summary>Derives the <see cref="BudgetPeriod"/> containing the given date.</summary>
    public static BudgetPeriod FromDate(DateTimeOffset date) => new(date.Year, date.Month);

    /// <summary>Returns the <see cref="BudgetPeriod"/> for the current UTC month.</summary>
    public static BudgetPeriod Current() => FromDate(DateTimeOffset.UtcNow);

    /// <summary>Returns the next consecutive <see cref="BudgetPeriod"/>.</summary>
    public BudgetPeriod Next()
    {
        var (y, m) = Month == 12 ? (Year + 1, 1) : (Year, Month + 1);
        return new BudgetPeriod(y, m);
    }

    /// <summary>Returns the previous consecutive <see cref="BudgetPeriod"/>.</summary>
    public BudgetPeriod Previous()
    {
        var (y, m) = Month == 1 ? (Year - 1, 12) : (Year, Month - 1);
        return new BudgetPeriod(y, m);
    }

    /// <summary>True when the given date falls within this period (same year+month).</summary>
    public bool Contains(DateTimeOffset date) => date.Year == Year && date.Month == Month;

    /// <summary>The UTC midnight timestamp at the start of this period.</summary>
    public DateTimeOffset StartDate => new(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The UTC midnight timestamp at the start of the next period.</summary>
    public DateTimeOffset EndDate => Next().StartDate;

    /// <summary>Returns a "YYYY-MM" representation.</summary>
    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
