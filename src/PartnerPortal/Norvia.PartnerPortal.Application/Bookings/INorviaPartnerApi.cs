using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings;

/// <summary>The Norvia Partner API, the only way the Partner portal reaches Norvia (ADR 0001).</summary>
public interface INorviaPartnerApi
{
    /// <summary>
    /// Sends a booking as the Persona, with its own token and subscription key. Norvia unreachable is an answer, not an
    /// error: <see cref="NorviaAnswer.NotReceived"/>.
    /// </summary>
    Task<NorviaAnswer> SubmitAsync(Persona persona, BookingRequest booking, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Carrier's answer to a booking request as the Persona, with its own token and subscription key. The Carrier is
    /// the one in the token, never named by the answer (ADR 0005). The Shipment number is for the portal's own
    /// trace, so it can be followed by the Shipment and the Carrier booking: it is not sent. Norvia unreachable is a reply, not an error:
    /// <see cref="CarrierAnswerResult.NotReceived"/>.
    /// </summary>
    Task<NorviaReply> AnswerAsync(Persona persona, string carrierBookingNumber, string shipmentNumber, CarrierAnswer answer, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Tracking event as the Carrier Persona, with its own token and subscription key. The Carrier is the one in the
    /// token, never named by the event (ADR 0005). The Shipment number is for the portal's own trace: it is not sent. Norvia
    /// unreachable is a reply, not an error: <see cref="TrackingResult.NotReceived"/>.
    /// </summary>
    Task<TrackingReply> SubmitTrackingAsync(Persona persona, string shipmentNumber, CarrierTrackingEvent trackingEvent, CancellationToken cancellationToken);
}
