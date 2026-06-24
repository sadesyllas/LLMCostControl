namespace LLMCostControl.Domain.Common;

/// <summary>
/// An amount of money in a specific currency. Money values can only be added or
/// subtracted when they share the same currency; mixing currencies throws.
/// </summary>
public sealed record Money
{
    /// <summary>The numeric amount.</summary>
    public decimal Amount { get; }

    /// <summary>The 3-letter ISO currency code (e.g. USD), upper-cased.</summary>
    public string Currency { get; }

    /// <summary>
    /// Creates a <see cref="Money"/> value with the given amount and currency.
    /// </summary>
    /// <param name="amount">The numeric amount.</param>
    /// <param name="currency">A 3-letter ISO currency code.</param>
    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency cannot be empty.", nameof(currency));
        }

        if (currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>Returns a zero-valued <see cref="Money"/> in the given currency.</summary>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>True when the amount is less than zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>
    /// Adds another <see cref="Money"/> of the same currency, returning the sum.
    /// </summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Subtracts another <see cref="Money"/> of the same currency, returning the
    /// difference.
    /// </summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies the amount by a scalar factor, keeping the currency.</summary>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    /// <summary>
    /// Compares this value to another of the same currency (throws on mismatch).
    /// </summary>
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>Greater-than comparison (same currency required).</summary>
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;
    /// <summary>Less-than comparison (same currency required).</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;
    /// <summary>Greater-than-or-equal comparison (same currency required).</summary>
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;
    /// <summary>Less-than-or-equal comparison (same currency required).</summary>
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <summary>Addition operator (same currency required).</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);
    /// <summary>Subtraction operator (same currency required).</summary>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot combine money in different currencies: {Currency} vs {other.Currency}.");
        }
    }

    /// <summary>Returns a formatted string "amount currency".</summary>
    public override string ToString() => $"{Amount:F4} {Currency}";
}
