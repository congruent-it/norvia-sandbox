using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.Draft;

/// <summary>A new booking for the Persona, filled in from its own address book so a demo moves quickly.</summary>
public sealed record DraftBookingQuery(PersonaId PersonaId) : IQuery<Result<BookingRequest>>;
