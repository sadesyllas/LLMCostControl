using System;
using System.Globalization;

namespace LLMCostControl.Domain.Common;

/// <summary>
/// Represents a concrete instance of a budget period (monthly or weekly) (§7).
/// </summary>
public readonly record struct BudgetPeriod
{
    /// <summary>The type of the budget period.</summary>
    public BudgetPeriodType PeriodType { get; }

    /// <summary>The start boundary (inclusive, UTC).</summary>
    public DateTimeOffset StartDate { get; }

    /// <summary>The end boundary (exclusive, UTC).</summary>
    public DateTimeOffset EndDate { get; }

    /// <summary>The string key identifying this period, e.g. YYYY-MM or YYYY-Www.</summary>
    public string Key { get; }

    /// <summary>The year this period starts in.</summary>
    public int Year => StartDate.Year;

    /// <summary>The month this period starts in.</summary>
    public int Month => StartDate.Month;

    /// <summary>
    /// Creates a Monthly BudgetPeriod for the specified year and month.
    /// </summary>
    public BudgetPeriod(int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentException("Month must be between 1 and 12.", nameof(month));
        }

        PeriodType = BudgetPeriodType.Monthly;
        StartDate = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        EndDate = StartDate.AddMonths(1);
        Key = $"{year:D4}-{month:D2}";
    }

    /// <summary>
    /// Private constructor to instantiate arbitrary periods.
    /// </summary>
    private BudgetPeriod(BudgetPeriodType periodType, DateTimeOffset startDate, DateTimeOffset endDate, string key)
    {
        PeriodType = periodType;
        StartDate = startDate;
        EndDate = endDate;
        Key = key;
    }

    /// <summary>
    /// Creates a monthly budget period.
    /// </summary>
    public static BudgetPeriod Monthly(int year, int month) => new(year, month);

    /// <summary>
    /// Creates a weekly budget period from a year and ISO week number.
    /// </summary>
    public static BudgetPeriod Weekly(int year, int week)
    {
        if (week is < 1 or > 53)
        {
            throw new ArgumentException("ISO Week must be between 1 and 53.", nameof(week));
        }

        var startOfWeek = GetFirstMondayOfIsoWeek(year, week);
        var endOfWeek = startOfWeek.AddDays(7);
        var key = $"{year:D4}-W{week:D2}";

        return new BudgetPeriod(BudgetPeriodType.Weekly, startOfWeek, endOfWeek, key);
    }

    /// <summary>
    /// Derives the monthly BudgetPeriod containing the given date.
    /// </summary>
    public static BudgetPeriod FromDate(DateTimeOffset date) => FromDate(date, BudgetPeriodType.Monthly);

    /// <summary>
    /// Derives the BudgetPeriod of the specified type containing the given date.
    /// </summary>
    public static BudgetPeriod FromDate(DateTimeOffset date, BudgetPeriodType type)
    {
        if (type == BudgetPeriodType.Monthly)
        {
            return new BudgetPeriod(date.Year, date.Month);
        }
        else
        {
            var (year, week) = GetIsoWeekAndYear(date);
            return Weekly(year, week);
        }
    }

    /// <summary>
    /// Returns the monthly BudgetPeriod for the current UTC month.
    /// </summary>
    public static BudgetPeriod Current() => FromDate(DateTimeOffset.UtcNow);

    /// <summary>
    /// Returns the BudgetPeriod of the specified type for the current UTC date.
    /// </summary>
    public static BudgetPeriod Current(BudgetPeriodType type) => FromDate(DateTimeOffset.UtcNow, type);

    /// <summary>
    /// Returns the next consecutive BudgetPeriod of the same type.
    /// </summary>
    public BudgetPeriod Next()
    {
        if (PeriodType == BudgetPeriodType.Monthly)
        {
            var nextStart = StartDate.AddMonths(1);
            return new BudgetPeriod(nextStart.Year, nextStart.Month);
        }
        else
        {
            var nextStart = StartDate.AddDays(7);
            var (year, week) = GetIsoWeekAndYear(nextStart);
            return Weekly(year, week);
        }
    }

    /// <summary>
    /// Returns the previous consecutive BudgetPeriod of the same type.
    /// </summary>
    public BudgetPeriod Previous()
    {
        if (PeriodType == BudgetPeriodType.Monthly)
        {
            var prevStart = StartDate.AddMonths(-1);
            return new BudgetPeriod(prevStart.Year, prevStart.Month);
        }
        else
        {
            var prevStart = StartDate.AddDays(-7);
            var (year, week) = GetIsoWeekAndYear(prevStart);
            return Weekly(year, week);
        }
    }

    /// <summary>
    /// True when the given date falls within [StartDate, EndDate).
    /// </summary>
    public bool Contains(DateTimeOffset date) => date >= StartDate && date < EndDate;

    /// <summary>
    /// Returns the string representation (the period Key).
    /// </summary>
    public override string ToString() => Key;

    private static DateTimeOffset GetFirstMondayOfIsoWeek(int year, int week)
    {
        var jan4 = new DateTimeOffset(year, 1, 4, 0, 0, 0, TimeSpan.Zero);
        var daysOffset = DayOfWeek.Monday - jan4.DayOfWeek;
        if (daysOffset > 0) daysOffset -= 7;
        var firstMonday = jan4.AddDays(daysOffset);
        return firstMonday.AddDays((week - 1) * 7);
    }

    private static (int Year, int Week) GetIsoWeekAndYear(DateTimeOffset date)
    {
        int week = ISOWeek.GetWeekOfYear(date.UtcDateTime);
        int year = ISOWeek.GetYear(date.UtcDateTime);
        return (year, week);
    }
}
