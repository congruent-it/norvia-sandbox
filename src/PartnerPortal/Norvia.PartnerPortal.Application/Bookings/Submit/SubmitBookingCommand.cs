using Ardalis.Result;
using Mediator;

namespace Norvia.PartnerPortal.Application.Bookings.Submit;

/// <summary>Sends a booking to Norvia as a Persona, and keeps it with Norvia's answer.</summary>
public sealed record SubmitBookingCommand(string PersonaId, BookingRequest Booking) : ICommand<Result<BookingView>>;
