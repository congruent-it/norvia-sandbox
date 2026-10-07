using Norvia.PartnerPortal.Application.ShipperTracking;
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

    /// <summary>
    /// Sends a Proof of delivery as the Carrier Persona, with its own token and subscription key, its files in the one body.
    /// The Carrier is the one in the token, never named by the Proof of delivery (ADR 0005). The Shipment number is for the
    /// portal's own trace: it is not sent. Norvia unreachable is a reply, not an error: <see cref="TrackingResult.NotReceived"/>.
    /// </summary>
    Task<TrackingReply> SubmitProofOfDeliveryAsync(Persona persona, string shipmentNumber, CarrierProofOfDelivery proof, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the Shipper Persona's Transport Orders as the Persona, with its own token and subscription key, newest
    /// collection first. A failure is an answer, not an error: <see cref="TrackingOutcome.Unavailable"/> when Norvia can't be reached.
    /// </summary>
    Task<TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>> ListTransportOrdersAsync(Persona persona, ShipperTrackingFilter filter, CancellationToken cancellationToken);

    /// <summary>Gets one of the Shipper Persona's Transport Orders by Norvia's number, with its progress and current Proof of delivery.</summary>
    Task<TrackingAnswer<ShipperTrackingDetail>> GetTransportOrderAsync(Persona persona, string transportOrderNumber, CancellationToken cancellationToken);

    /// <summary>Finds one of the Shipper Persona's Transport Orders by the Shipper's own reference; answers as <see cref="GetTransportOrderAsync"/>.</summary>
    Task<TrackingAnswer<ShipperTrackingDetail>> FindTransportOrderAsync(Persona persona, string shipperReference, CancellationToken cancellationToken);

    /// <summary>Downloads a file of the Transport Order's current Proof of delivery, as Norvia streams it.</summary>
    Task<TrackingAnswer<ShipperProofFileContent>> DownloadProofOfDeliveryFileAsync(Persona persona, string transportOrderNumber, Guid fileId, CancellationToken cancellationToken);
}
