namespace Norvia.Tms.Domain.RateCards;

/// <summary>The ERP's ID of a Rate Card, such as <c>RC-0001</c>.</summary>
public sealed record RateCardId
{
    public RateCardId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
