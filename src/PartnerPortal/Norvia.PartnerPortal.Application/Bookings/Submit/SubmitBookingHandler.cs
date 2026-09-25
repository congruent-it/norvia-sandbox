using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.Submit;

public sealed class SubmitBookingHandler(IRepository<Booking> repository, INorviaPartnerApi partnerApi, IPersonaDirectory personas, TimeProvider time)
    : ICommandHandler<SubmitBookingCommand, Result<BookingView>>
{
    public async ValueTask<Result<BookingView>> Handle(SubmitBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(new PersonaId(command.PersonaId)) is not { } persona)
        {
            return Result.NotFound($"There is no Persona '{command.PersonaId}'.");
        }

        var sentAt = time.GetUtcNow();
        var answer = await partnerApi.SubmitAsync(persona, command.Booking, cancellationToken);
        var booking = Booking.Record(persona.Id, command.Booking.ShipperReference, sentAt, answer);
        await repository.AddAsync(booking, cancellationToken);

        return BookingView.From(booking);
    }
}
