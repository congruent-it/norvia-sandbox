using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

public sealed class DraftBookingFromPdfHandler(IPersonaDirectory personas, INorviaPartnerApi partnerApi)
    : IQueryHandler<DraftBookingFromPdfQuery, Result<BookingDraftAnswer>>
{
    public async ValueTask<Result<BookingDraftAnswer>> Handle(DraftBookingFromPdfQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (personas.Find(query.PersonaId) is not { } persona)
        {
            return Result.NotFound($"There is no Persona '{query.PersonaId}'.");
        }

        return await partnerApi.DraftBookingFromPdfAsync(persona, query.Pdf, cancellationToken);
    }
}
