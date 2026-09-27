namespace Norvia.Tms.Domain.Carriers;

public sealed record CarrierId
{
    public CarrierId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
