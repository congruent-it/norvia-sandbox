using System.Xml;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Api.Trips;

/// <summary>A Trip version in the Canonical Model's <c>TripDispatched</c> shape, as the Trip dispatch Logic App sends it. The event's own envelope (its ID, type and time) is not the Driver app's concern.</summary>
internal sealed record TripVersionRequest(
    string TripNumber,
    int Version,
    string CorrelationId,
    DateOnly Day,
    TimeOnly PlannedStart,
    string PlannedDrivingDuration,
    TripVehicle Vehicle,
    RequestDriver Driver,
    IReadOnlyList<TripStop> Stops)
{
    public bool TryToCommand(out ReceiveTripVersionCommand command)
    {
        command = null!;
        TimeSpan duration;
        try
        {
            duration = XmlConvert.ToTimeSpan(PlannedDrivingDuration);
        }
        catch (FormatException)
        {
            return false;
        }

        command = new ReceiveTripVersionCommand(
            TripNumber, Version, Driver.DriverId,
            new TripDetails(Day, PlannedStart, duration, Vehicle, Driver.Name, CorrelationId, Stops));
        return true;
    }
}

internal sealed record RequestDriver(string DriverId, string Name);
