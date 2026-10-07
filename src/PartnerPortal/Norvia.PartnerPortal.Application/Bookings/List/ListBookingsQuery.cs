using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.List;

/// <summary>The bookings a Persona sent, the latest first.</summary>
public sealed record ListBookingsQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<BookingView>>;
