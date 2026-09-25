using Mediator;

namespace Norvia.PartnerPortal.Application.Bookings.List;

/// <summary>The bookings a Persona sent, the latest first.</summary>
public sealed record ListBookingsQuery(string PersonaId) : IQuery<IReadOnlyList<BookingView>>;
