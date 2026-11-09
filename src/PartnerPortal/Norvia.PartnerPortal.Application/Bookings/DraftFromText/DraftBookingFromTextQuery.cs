using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>Asks Norvia's Intake agent for a Booking draft from the text the Shipper Persona pasted. Reads, stores and changes nothing.</summary>
public sealed record DraftBookingFromTextQuery(PersonaId PersonaId, string Text) : IQuery<Result<BookingDraftAnswer>>;
