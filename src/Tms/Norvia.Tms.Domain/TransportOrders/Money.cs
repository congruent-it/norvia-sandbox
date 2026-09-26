namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// An amount in euros without VAT, the only currency Rating charges in; the ERP adds VAT when it invoices.
/// </summary>
public readonly record struct Money(decimal Amount)
{
    public const string Currency = "EUR";

    public static Money Sum(IEnumerable<Money> amounts) => new(amounts.Sum(money => money.Amount));

    public bool IsBelow(Money other) => Amount < other.Amount;

    /// <summary>The difference between this amount and another.</summary>
    public Money Minus(Money other) => new(Amount - other.Amount);

    /// <summary>The amount multiplied, unrounded, so rounding happens once, on the charge line.</summary>
    public Money Times(decimal factor) => new(Amount * factor);

    /// <summary>The amount rounded to cents, half away from zero, as every charge line is.</summary>
    public Money RoundedToCents() => new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero));
}
