namespace LLMCostControl.Domain.Common;

public readonly record struct BudgetPeriod
{
    public int Year { get; }
    public int Month { get; }

    public BudgetPeriod(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentException("Month must be between 1 and 12.", nameof(month));
        }

        Year = year;
        Month = month;
    }

    public static BudgetPeriod FromDate(DateTimeOffset date) => new(date.Year, date.Month);

    public static BudgetPeriod Current() => FromDate(DateTimeOffset.UtcNow);

    public BudgetPeriod Next()
    {
        var (y, m) = Month == 12 ? (Year + 1, 1) : (Year, Month + 1);
        return new BudgetPeriod(y, m);
    }

    public BudgetPeriod Previous()
    {
        var (y, m) = Month == 1 ? (Year - 1, 12) : (Year, Month - 1);
        return new BudgetPeriod(y, m);
    }

    public bool Contains(DateTimeOffset date) => date.Year == Year && date.Month == Month;

    public DateTimeOffset StartDate => new(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset EndDate => Next().StartDate;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
