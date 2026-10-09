namespace Norvia.Tms.Domain.Fleet;

public sealed record DriverId
{
    public DriverId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
