using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

public sealed class DraftBookingFromTextHandler(IPersonaDirectory personas, INorviaPartnerApi partnerApi)
    : IQueryHandler<DraftBookingFromTextQuery, Result<BookingDraftAnswer>>
{
    public async ValueTask<Result<BookingDraftAnswer>> Handle(DraftBookingFromTextQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (personas.Find(query.PersonaId) is not { } persona)
        {
            return Result.NotFound($"There is no Persona '{query.PersonaId}'.");
        }

        return await partnerApi.DraftBookingFromTextAsync(persona, query.Text, cancellationToken);
    }
}
