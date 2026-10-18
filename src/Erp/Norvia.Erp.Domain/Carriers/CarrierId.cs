namespace Norvia.Erp.Domain.Carriers;

/// <summary>The Carrier ID the TMS uses too, such as <c>CAR-0001</c>.</summary>
public readonly record struct CarrierId
{
    public CarrierId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
