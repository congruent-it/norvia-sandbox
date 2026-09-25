using Ardalis.Specification;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.Bookings;

/// <summary>The bookings a Persona sent, the latest first.</summary>
public sealed class BookingsOfPersonaSpec : Specification<Booking>
{
    public BookingsOfPersonaSpec(PersonaId personaId)
    {
        PersonaId = personaId;
        Query.Where(booking => booking.PersonaId == personaId)
            .OrderByDescending(booking => booking.SentAt)
            .AsNoTracking();
    }

    public PersonaId PersonaId { get; }
}
