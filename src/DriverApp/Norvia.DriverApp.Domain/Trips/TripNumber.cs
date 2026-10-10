namespace Norvia.DriverApp.Domain.Trips;

/// <summary>A Trip's number, such as <c>TR-0000007</c>, which stays the same through every version.</summary>
public readonly record struct TripNumber(string Value)
{
    public override string ToString() => Value;
}
