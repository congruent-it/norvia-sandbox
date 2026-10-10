namespace Norvia.DriverApp.Domain.Trips;

/// <summary>A Driver's ID, such as <c>DRV-0001</c>, as the seed file and the Trip versions name it.</summary>
public readonly record struct DriverId(string Value)
{
    public override string ToString() => Value;
}
