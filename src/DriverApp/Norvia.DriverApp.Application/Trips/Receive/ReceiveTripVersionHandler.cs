using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Settings;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips.Receive;

public sealed class ReceiveTripVersionHandler(IRepository<DriverTrip> trips, IReadRepository<DriverAppSettings> settings)
    : ICommandHandler<ReceiveTripVersionCommand, Result<TripVersionOutcome>>
{
    public async ValueTask<Result<TripVersionOutcome>> Handle(ReceiveTripVersionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await settings.GetByIdAsync(DriverAppSettings.SingleId, cancellationToken) is { Unavailable: true })
        {
            return Result.Unavailable("The Driver app is unavailable.");
        }

        var held = await trips.GetByIdAsync(new TripNumber(command.TripNumber), cancellationToken);
        if (held is null)
        {
            await trips.AddAsync(new DriverTrip(command.TripNumber, command.Version, command.DriverId, command.Details), cancellationToken);
            return TripVersionOutcome.Stored;
        }

        if (!held.ReplaceWith(command.Version, command.DriverId, command.Details))
        {
            return TripVersionOutcome.Ignored;
        }

        await trips.UpdateAsync(held, cancellationToken);
        return TripVersionOutcome.Stored;
    }
}
