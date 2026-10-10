using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Discard;

public sealed class DiscardDraftTripHandler(IRepository<Trip> trips) : ICommandHandler<DiscardDraftTripCommand, Result>
{
    public async ValueTask<Result> Handle(DiscardDraftTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TripNumber.TryParse(command.Number, out var number)
            || await trips.FirstOrDefaultAsync(new TripByNumberSpec(number), cancellationToken) is not { } trip)
        {
            return TripRefused.Because(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.CanBeDiscarded() is { } notDraft)
        {
            return TripRefused.Because(notDraft);
        }

        await trips.DeleteAsync(trip, cancellationToken);
        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
