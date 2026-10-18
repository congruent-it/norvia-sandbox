namespace Norvia.Erp.Domain.RateCards;

/// <summary>The ID of a Rate Card or a Carrier Rate Card as the seed file spells it, such as <c>RC-0001</c> or <c>CRC-0001</c>.</summary>
public readonly record struct RateCardId
{
    public RateCardId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
