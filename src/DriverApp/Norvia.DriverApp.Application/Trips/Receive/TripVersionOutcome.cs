namespace Norvia.DriverApp.Application.Trips.Receive;

public enum TripVersionOutcome
{
    /// <summary>The version is now the Trip's latest.</summary>
    Stored,

    /// <summary>The Driver app already held this version or a later one, so nothing changed.</summary>
    Ignored,
}
