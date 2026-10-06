using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

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

    /// <summary>How the Carrier Persona answers its booking requests now; null when there is no such Carrier Persona.</summary>
    Task<CarrierAnswerSettingsView?> GetCarrierAnswerSettingsAsync(string personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes how the Carrier Persona answers the requests that reach it from now on, and returns the new settings;
    /// null when there is no such Carrier Persona. A delay out of range throws, as the page limits it.
    /// </summary>
    Task<CarrierAnswerSettingsView?> ChangeCarrierAnswerSettingsAsync(string personaId, CarrierAnswerSettingsView settings, CancellationToken cancellationToken);

    /// <summary>The Carrier Persona's Shipments, Booked or later and Delivered ones too, the latest first.</summary>
    Task<IReadOnlyList<CarrierShipmentView>> ListCarrierShipmentsAsync(string personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Tracking event for one of the Carrier Persona's Shipments to Norvia, and returns what the Partner API replied
    /// and how the Shipment now stands; null when there is no such Carrier Persona or Shipment.
    /// </summary>
    Task<SentTrackingEvent?> SendTrackingEventAsync(string personaId, string carrierBookingNumber, TrackingEventType type, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Proof of delivery for one of the Carrier Persona's Shipments to Norvia, and returns what the Partner API
    /// replied; null when there is no such Carrier Persona or Shipment.
    /// </summary>
    Task<SentProofOfDelivery?> SendProofOfDeliveryAsync(
        string personaId, string carrierBookingNumber, ProofOfDeliveryDraft draft, CancellationToken cancellationToken);

    /// <summary>How the Carrier Persona sends Tracking events now; null when there is no such Carrier Persona.</summary>
    Task<CarrierTrackingSettingsView?> GetCarrierTrackingSettingsAsync(string personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes how the Carrier Persona sends Tracking events for the Shipments it confirms from now on, and returns the new
    /// settings; null when there is no such Carrier Persona. A delay out of range throws, as the page limits it.
    /// </summary>
    Task<CarrierTrackingSettingsView?> ChangeCarrierTrackingSettingsAsync(string personaId, CarrierTrackingSettingsView settings, CancellationToken cancellationToken);
}
