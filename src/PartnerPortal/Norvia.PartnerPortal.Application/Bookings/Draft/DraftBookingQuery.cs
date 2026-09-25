using Ardalis.Result;
using Mediator;

namespace Norvia.PartnerPortal.Application.Bookings.Draft;

/// <summary>A new booking for the Persona, filled in from its own address book so a demo moves quickly.</summary>
public sealed record DraftBookingQuery(string PersonaId) : IQuery<Result<BookingRequest>>;
