using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>What the portal's pages ask of the application: the Personas, and each Persona's bookings.</summary>
public interface IPartnerPortal
{
    Task<IReadOnlyList<PersonaView>> ListPersonasAsync(CancellationToken cancellationToken);

    /// <summary>A new booking for the Persona, filled in from its address book; null when there is no such Persona.</summary>
    Task<BookingRequest?> DraftBookingAsync(string personaId, CancellationToken cancellationToken);

    /// <summary>Sends the booking to Norvia as the Persona and returns Norvia's answer; null when there is no such Persona.</summary>
    Task<BookingView?> SubmitBookingAsync(string personaId, BookingRequest booking, CancellationToken cancellationToken);

    /// <summary>The bookings the Persona sent, the latest first.</summary>
    Task<IReadOnlyList<BookingView>> ListBookingsAsync(string personaId, CancellationToken cancellationToken);

    /// <summary>The booking requests in the Carrier Persona's inbox, the latest first.</summary>
    Task<IReadOnlyList<CarrierBookingRequestView>> ListCarrierBookingRequestsAsync(string personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the Carrier Persona's answer to one of the requests in its inbox to Norvia, and returns what Norvia replied and
    /// how the inbox now has the request; null when there is no such Carrier Persona or request.
    /// </summary>
    Task<AnsweredCarrierBooking?> AnswerCarrierBookingAsync(string personaId, string carrierBookingNumber, CarrierAnswer answer, CancellationToken cancellationToken);
}
