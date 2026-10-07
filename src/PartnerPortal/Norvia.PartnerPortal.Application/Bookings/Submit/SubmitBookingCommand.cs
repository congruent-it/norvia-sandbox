using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.Submit;

/// <summary>Sends a booking to Norvia as a Persona, and keeps it with Norvia's answer.</summary>
public sealed record SubmitBookingCommand(PersonaId PersonaId, BookingRequest Booking) : ICommand<Result<BookingView>>;
