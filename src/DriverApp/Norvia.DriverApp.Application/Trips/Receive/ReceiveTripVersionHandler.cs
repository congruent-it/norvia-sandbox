using System.Xml;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Application.Faults;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.Receive;

public sealed class ReceiveTripVersionHandler(IRepository<DriverTrip> trips, IDriverAppFaults faults)
    : ICommandHandler<ReceiveTripVersionCommand, Result<TripVersionOutcome>>
{
    public async ValueTask<Result<TripVersionOutcome>> Handle(ReceiveTripVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The demo fault refuses everything, a malformed version included.
        if (faults.IsUnavailable)
        {
            return Result.Unavailable("The Driver app is unavailable.");
        }

        if (ReceiveTripVersionValidator.Validate(command) is { Count: > 0 } errors)
        {
            return Result.Invalid(errors);
        }

        var tripNumber = new TripNumber(command.TripNumber);
        var driverId = new DriverId(command.DriverId);
        var details = new TripDetails(
            command.Day,
            command.PlannedStart,
            XmlConvert.ToTimeSpan(command.PlannedDrivingDuration),
            new TripVehicle(command.Vehicle.VehicleId, command.Vehicle.Registration, command.Vehicle.VehicleType),
            command.DriverName,
            command.CorrelationId,
            [.. command.Stops.Select(stop => stop.ToDomain())]);

        var stored = await trips.GetByIdAsync(tripNumber, cancellationToken);
        if (stored is null)
        {
            await trips.AddAsync(new DriverTrip(tripNumber, command.Version, driverId, details), cancellationToken);
            return TripVersionOutcome.Stored;
        }

        if (!stored.ReplaceWith(command.Version, driverId, details))
        {
            return TripVersionOutcome.Ignored;
        }

        await trips.UpdateAsync(stored, cancellationToken);
        return TripVersionOutcome.Stored;
    }
}
