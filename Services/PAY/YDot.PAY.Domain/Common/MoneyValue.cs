using System.Globalization;

namespace YDot.PAY.Domain.Common;

/// <summary>
/// An amount and the currency it is denominated in, as one value.
///
/// WHY THE PAIR IS ONE TYPE. A decimal on its own is not money: 500 is a large gift in pounds
/// and a small one in yen, and the moment an amount travels without its currency somebody
/// eventually adds two of them together. Binding them means the invariant "never compare or
/// combine amounts in different currencies" is stated once, here, instead of being remembered
/// at every call site.
///
/// It is mapped as an OWNED TYPE rather than a foreign key, so each amount column sits beside
/// its own currency column on the same row - which is what makes a single-row read enough to
/// render a figure correctly.
/// </summary>
public sealed record MoneyValue
{
    private MoneyValue(decimal amount, string currencyCode)
    {
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    /// <summary>EF needs a parameterless constructor to materialise an owned type.</summary>
    private MoneyValue()
    {
        CurrencyCode = string.Empty;
    }

    public decimal Amount { get; private set; }

    /// <summary>ISO 4217, upper-cased. Matches the code on the IAM currency master.</summary>
    public string CurrencyCode { get; private set; }

    public static MoneyValue Create(decimal amount, string currencyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), "A monetary amount cannot be negative. Model a refund as its own record.");
        }

        return new MoneyValue(amount, currencyCode.Trim().ToUpperInvariant());
    }

    /// <summary>Zero in the same currency. The starting point for a running total.</summary>
    public static MoneyValue Zero(string currencyCode) => Create(0m, currencyCode);

    /// <summary>
    /// The same amount, as a SEPARATE INSTANCE, for a different owner.
    ///
    /// WHY THIS IS NOT POINTLESS ON A RECORD. This is an EF OWNED TYPE, and an owned entity's
    /// identity is its owner - so one CLR instance assigned to two owners is one object that EF
    /// tracks as two different entity types at once. It warns about exactly that:
    ///
    ///     The same entity is being tracked as different entity types 'Receipt.Amount#MoneyValue'
    ///     and 'Donation.Amount#MoneyValue' with defining navigations. If a property value
    ///     changes, it will result in two store changes, which might not be the desired outcome.
    ///
    /// AND IT IS NOT MERELY UNTIDY. Recording a captured donation passed ONE instance into
    /// PaymentEvent.Amount, PaymentAttempt.CapturedAmount, Donation.Amount and then Receipt.Amount.
    /// The save that followed emitted an update for every one of those owners from that single
    /// object, and the ones whose rows did not match answered "expected to affect 1 row(s), but
    /// actually affected 0" - a DbUpdateConcurrencyException that rolled the whole transaction
    /// back and unwound a donation whose money had already been taken.
    ///
    /// So: assign a copy whenever an amount moves to a second owner. Reading one is free; it is
    /// only STORING the same instance twice that is the error.
    /// </summary>
    public MoneyValue Copy() => new(Amount, CurrencyCode);

    /// <summary>
    /// Adds two amounts.
    ///
    /// REFUSES A CURRENCY MISMATCH rather than converting. There is no exchange rate in scope
    /// here, and silently treating 100 USD plus 100 INR as 200 of something is the exact bug
    /// this type exists to prevent.
    /// </summary>
    public MoneyValue Add(MoneyValue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);

        return new MoneyValue(Amount + other.Amount, CurrencyCode);
    }

    /// <summary>Subtracts, refusing to go below zero - a negative balance is a modelling error.</summary>
    public MoneyValue Subtract(MoneyValue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);

        if (other.Amount > Amount)
        {
            throw new InvalidOperationException(
                $"Cannot subtract {other} from {this}: the result would be negative.");
        }

        return new MoneyValue(Amount - other.Amount, CurrencyCode);
    }

    public bool IsZero => Amount == 0m;

    public bool IsSameCurrencyAs(MoneyValue other) =>
        other is not null
        && string.Equals(CurrencyCode, other.CurrencyCode, StringComparison.Ordinal);

    private void EnsureSameCurrency(MoneyValue other)
    {
        if (!IsSameCurrencyAs(other))
        {
            throw new InvalidOperationException(
                $"Cannot combine {CurrencyCode} with {other.CurrencyCode}. "
                + "Amounts in different currencies are not comparable without a rate.");
        }
    }

    public override string ToString() =>
        $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)} {CurrencyCode}";
}
