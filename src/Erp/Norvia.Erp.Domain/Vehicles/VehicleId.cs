namespace Norvia.Erp.Domain.Vehicles;

/// <summary>The Vehicle ID the TMS uses too, such as <c>VEH-0001</c>.</summary>
public readonly record struct VehicleId
{
    public VehicleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
