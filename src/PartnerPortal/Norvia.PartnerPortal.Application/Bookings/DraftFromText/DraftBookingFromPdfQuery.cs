using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>Asks Norvia's Intake agent for a Booking draft from a PDF the Shipper Persona uploaded. The PDF is sent and never kept or logged. Reads, stores and changes nothing.</summary>
public sealed record DraftBookingFromPdfQuery(PersonaId PersonaId, byte[] Pdf) : IQuery<Result<BookingDraftAnswer>>;
