namespace Norvia.Tms.Domain.RateCards;

/// <summary>The name of a Zone within a Rate Card version, by which its Lanes name their origin and destination.</summary>
public sealed record ZoneName
{
    public ZoneName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
