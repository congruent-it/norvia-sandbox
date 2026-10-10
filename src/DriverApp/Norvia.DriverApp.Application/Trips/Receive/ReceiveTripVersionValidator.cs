using System.Text.RegularExpressions;
using System.Xml;
using Ardalis.Result;

namespace Norvia.DriverApp.Application.Trips.Receive;

/// <summary>Checks a Trip version against the Canonical Model's <c>trip-dispatched</c> schema, which the request's binding can't: its patterns, its bounds and its enums.</summary>
internal static partial class ReceiveTripVersionValidator
{
    private static readonly string[] VehicleTypes = ["Van", "RigidTruck", "TractorWithTrailer"];
    private static readonly TimeSpan LongestDrive = TimeSpan.FromHours(24);

    public static List<ValidationError> Validate(ReceiveTripVersionCommand command)
    {
        var errors = new List<ValidationError>();
        if (!TripNumberPattern().IsMatch(command.TripNumber))
        {
            errors.Add(new("tripNumber", "Must look like TR-0000007."));
        }

        if (command.Version < 1)
        {
            errors.Add(new("version", "Must be 1 or more."));
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            errors.Add(new("correlationId", "Is required."));
        }

        if (string.IsNullOrWhiteSpace(command.DriverId))
        {
            errors.Add(new("driver.driverId", "Is required."));
        }

        if (string.IsNullOrWhiteSpace(command.DriverName))
        {
            errors.Add(new("driver.name", "Is required."));
        }

        if (!IsADurationOfAtMostADay(command.PlannedDrivingDuration))
        {
            errors.Add(new("plannedDrivingDuration", "Must be an ISO 8601 duration of more than nothing and at most 24 hours, such as PT6H."));
        }

        if (string.IsNullOrWhiteSpace(command.Vehicle.VehicleId) || string.IsNullOrWhiteSpace(command.Vehicle.Registration))
        {
            errors.Add(new("vehicle", "Needs a vehicleId and a registration."));
        }

        if (!VehicleTypes.Contains(command.Vehicle.VehicleType))
        {
            errors.Add(new("vehicle.vehicleType", $"Must be one of {string.Join(", ", VehicleTypes)}."));
        }

        foreach (var (stop, index) in command.Stops.Select((stop, index) => (stop, index)))
        {
            ValidateStop(stop, $"stops[{index}]", errors);
        }

        return errors;
    }

    private static void ValidateStop(StopDto stop, string path, List<ValidationError> errors)
    {
        if (stop.Sequence < 1)
        {
            errors.Add(new($"{path}.sequence", "Must be 1 or more."));
        }

        if (!ShipmentNumberPattern().IsMatch(stop.ShipmentNumber))
        {
            errors.Add(new($"{path}.shipmentNumber", "Must look like SH-0000012."));
        }

        if (!TransportOrderNumberPattern().IsMatch(stop.TransportOrderNumber))
        {
            errors.Add(new($"{path}.transportOrderNumber", "Must look like TO-0000003."));
        }

        if (string.IsNullOrWhiteSpace(stop.ShipperId))
        {
            errors.Add(new($"{path}.shipperId", "Is required."));
        }
    }

    private static bool IsADurationOfAtMostADay(string value)
    {
        try
        {
            var duration = XmlConvert.ToTimeSpan(value);
            return duration > TimeSpan.Zero && duration <= LongestDrive;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex("^TR-[0-9]{7}$")]
    private static partial Regex TripNumberPattern();

    [GeneratedRegex("^SH-[0-9]{7}$")]
    private static partial Regex ShipmentNumberPattern();

    [GeneratedRegex("^TO-[0-9]{7}$")]
    private static partial Regex TransportOrderNumberPattern();
}
