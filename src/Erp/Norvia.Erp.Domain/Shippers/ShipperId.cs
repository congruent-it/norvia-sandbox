namespace Norvia.Erp.Domain.Shippers;

/// <summary>The Shipper ID the TMS uses too, such as <c>SHP-0001</c>.</summary>
public readonly record struct ShipperId
{
    public ShipperId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
