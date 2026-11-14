namespace Norvia.Erp.Domain.Drivers;

/// <summary>The Driver ID the TMS uses too, such as <c>DRV-0001</c>.</summary>
public readonly record struct DriverId
{
    public DriverId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
