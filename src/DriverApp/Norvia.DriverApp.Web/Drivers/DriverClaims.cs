namespace Norvia.DriverApp.Web.Drivers;

/// <summary>The claim and policy names the Driver app signs Drivers in with.</summary>
public static class DriverClaims
{
    /// <summary>The claim that carries the Driver ID (<c>DRV-0001</c>): the one thing the Driver app takes from the token about who is driving (ADR 0005, ADR 0037).</summary>
    public const string DriverId = "driverId";

    /// <summary>The claim that carries the name of the Driver the presenter picked (ADR 0047). A Driver's own token has none: their name is the user's.</summary>
    public const string DriverName = "driverName";

    /// <summary>The policy every screen requires: a signed-in user whose token carries a Driver ID.</summary>
    public const string DriverPolicy = "Driver";
}
