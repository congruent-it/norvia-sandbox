namespace Norvia.Simulator.Application.Ports;

/// <summary>A Trip's status in the Driver app: Dispatched until the TMS has recorded the Driver's Start Trip, then In progress, then Completed once it has recorded the End Trip (P12).</summary>
public enum DriverTripStatus
{
    Dispatched,
    InProgress,
    Completed,
}
