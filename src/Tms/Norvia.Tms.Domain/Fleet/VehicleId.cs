namespace Norvia.Tms.Domain.Fleet;

public sealed record VehicleId
{
    public VehicleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
