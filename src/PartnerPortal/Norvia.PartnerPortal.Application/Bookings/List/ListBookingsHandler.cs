using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.List;

public sealed class ListBookingsHandler(IReadRepository<Booking> repository) : IQueryHandler<ListBookingsQuery, IReadOnlyList<BookingView>>
{
    public async ValueTask<IReadOnlyList<BookingView>> Handle(ListBookingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var bookings = await repository.ListAsync(new BookingsOfPersonaSpec(query.PersonaId), cancellationToken);

        return [.. bookings.Select(BookingView.From)];
    }
}
