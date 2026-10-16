namespace Norvia.Simulator.Application.Ports;

/// <summary>What a Driver sees of one of their Trips in the Driver app: the latest version it holds, how far the TMS has recorded it, and how many reports were refused or never recorded.</summary>
public sealed record DriverTripSnapshot(string TripNumber, int Version, DriverTripStatus Status, int ReportsNotRecorded);

/// <summary>A Trip's status in the Driver app: Dispatched until the TMS has recorded the Driver's Start Trip, then In progress, then Completed once it has recorded the End Trip (P12).</summary>
public enum DriverTripStatus
{
    Dispatched,
    InProgress,
    Completed,
}
