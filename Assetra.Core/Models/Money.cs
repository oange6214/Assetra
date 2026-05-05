using System.Globalization;

namespace Assetra.Core.Models;

/// <summary>
/// Currency-tagged amount. Same-currency arithmetic is allowed via operators;
/// cross-currency operations require explicit conversion through the multi-currency
/// valuation service (this type intentionally has NO implicit conversion to <see cref="decimal"/>
/// to keep currency context attached).
/// </summary>
/// <remarks>
/// <para>
/// Introduced as the foundation for code-review item M1. Initial scope: pure value type
/// with arithmetic, comparison, and parsing helpers — no callers migrated yet. Migration
/// of <c>IBalanceQueryService</c> / repositories / display layer is tracked separately.
/// </para>
/// <para>
/// Currency code is normalised to upper-case at construction so equality is case-insensitive
/// across "twd" / "TWD".
/// </para>
/// </remarks>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public bool IsZero => Amount == 0m;
    public bool IsPositive => Amount > 0m;
    public bool IsNegative => Amount < 0m;

    public Money Negate() => new(-Amount, Currency);
    public Money Abs() => Amount < 0m ? new Money(-Amount, Currency) : this;

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static Money operator -(Money m) => m.Negate();

    public static Money operator *(Money m, decimal scalar) => new(m.Amount * scalar, m.Currency);
    public static Money operator *(decimal scalar, Money m) => m * scalar;

    public static Money operator /(Money m, decimal scalar)
    {
        if (scalar == 0m) throw new DivideByZeroException();
        return new Money(m.Amount / scalar, m.Currency);
    }

    public static bool operator <(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount <  b.Amount; }
    public static bool operator >(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount >  b.Amount; }
    public static bool operator <=(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount <= b.Amount; }
    public static bool operator >=(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount >= b.Amount; }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cannot operate on Money values with different currencies: {a.Currency} vs {b.Currency}. " +
                "Convert through IMultiCurrencyValuationService first.");
    }

    public override string ToString() =>
        $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";
}
