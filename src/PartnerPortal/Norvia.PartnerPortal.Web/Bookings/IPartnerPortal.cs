using Ardalis.Result;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Application.CarrierInvoices.Draft;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Application.DriverSignIns;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperInvoices;
using Norvia.PartnerPortal.Application.ShipperNotifications;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>What the portal's pages ask of the application: the Personas, and each Persona's bookings.</summary>
public interface IPartnerPortal
{
    Task<IReadOnlyList<PersonaView>> ListPersonasAsync(CancellationToken cancellationToken);

    /// <summary>A new booking for the Persona, filled in from its address book; null when there is no such Persona.</summary>
    Task<BookingRequest?> DraftBookingAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>Sends the booking to Norvia as the Persona and returns Norvia's answer; null when there is no such Persona.</summary>
    Task<BookingView?> SubmitBookingAsync(PersonaId personaId, BookingRequest booking, CancellationToken cancellationToken);

    /// <summary>The bookings the Persona sent, the latest first.</summary>
    Task<IReadOnlyList<BookingView>> ListBookingsAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>The booking requests in the Carrier Persona's inbox, the latest first.</summary>
    Task<IReadOnlyList<CarrierBookingRequestView>> ListCarrierBookingRequestsAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the Carrier Persona's answer to one of the requests in its inbox to Norvia, and returns what Norvia replied and
    /// how the inbox now has the request; null when there is no such Carrier Persona or request.
    /// </summary>
    Task<AnsweredCarrierBooking?> AnswerCarrierBookingAsync(PersonaId personaId, string carrierBookingNumber, CarrierAnswer answer, CancellationToken cancellationToken);

    /// <summary>How the Carrier Persona answers its booking requests now; null when there is no such Carrier Persona.</summary>
    Task<CarrierAnswerSettingsView?> GetCarrierAnswerSettingsAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes how the Carrier Persona answers the requests that reach it from now on, and returns the new settings;
    /// null when there is no such Carrier Persona. A delay out of range throws, as the page limits it.
    /// </summary>
    Task<CarrierAnswerSettingsView?> ChangeCarrierAnswerSettingsAsync(PersonaId personaId, CarrierAnswerSettingsView settings, CancellationToken cancellationToken);

    /// <summary>The Carrier Persona's Shipments, Booked or later and Delivered ones too, the latest first.</summary>
    Task<IReadOnlyList<CarrierShipmentView>> ListCarrierShipmentsAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Tracking event for one of the Carrier Persona's Shipments to Norvia, and returns what the Partner API replied
    /// and how the Shipment now stands; null when there is no such Carrier Persona or Shipment.
    /// </summary>
    Task<SentTrackingEvent?> SendTrackingEventAsync(PersonaId personaId, string carrierBookingNumber, TrackingEventType type, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Proof of delivery for one of the Carrier Persona's Shipments to Norvia, and returns what the Partner API
    /// replied; null when there is no such Carrier Persona or Shipment.
    /// </summary>
    Task<SentProofOfDelivery?> SendProofOfDeliveryAsync(
        PersonaId personaId, string carrierBookingNumber, ProofOfDeliveryDraft draft, CancellationToken cancellationToken);

    /// <summary>How the Carrier Persona sends Tracking events now; null when there is no such Carrier Persona.</summary>
    Task<CarrierTrackingSettingsView?> GetCarrierTrackingSettingsAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes how the Carrier Persona sends Tracking events for the Shipments it confirms from now on, and returns the new
    /// settings; null when there is no such Carrier Persona. A delay out of range throws, as the page limits it.
    /// </summary>
    Task<CarrierTrackingSettingsView?> ChangeCarrierTrackingSettingsAsync(PersonaId personaId, CarrierTrackingSettingsView settings, CancellationToken cancellationToken);

    /// <summary>
    /// The Carrier Persona's draft Carrier invoice, built from its own bookings and Tracking records: no call to Norvia.
    /// Null when there is no such Carrier Persona.
    /// </summary>
    Task<CarrierInvoiceDraftView?> GetCarrierInvoiceDraftAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a Carrier invoice as the Carrier Persona to Norvia and returns what the Partner API replied, with the result of
    /// each line. Not found when there is no such Carrier Persona; invalid, with the reason, when the invoice breaks the rules
    /// the portal checks before it sends.
    /// </summary>
    Task<Result<SubmittedCarrierInvoice>> SubmitCarrierInvoiceAsync(
        PersonaId personaId, string invoiceNumber, DateOnly invoiceDate, IReadOnlyList<CarrierInvoiceLineDraft> lines, CancellationToken cancellationToken);

    /// <summary>The Carrier invoices the Carrier Persona sent, the latest first, each with the outcome per line once Norvia told it.</summary>
    Task<IReadOnlyList<CarrierInvoiceView>> ListCarrierInvoicesAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>The invoices and Credit notes in the Shipper Persona's inbox, the latest first.</summary>
    Task<IReadOnlyList<ShipperInvoiceView>> ListShipperInvoicesAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>The notifications in the Shipper Persona's inbox, the latest first.</summary>
    Task<IReadOnlyList<ShipperNotificationView>> ListShipperNotificationsAsync(PersonaId personaId, CancellationToken cancellationToken);

    /// <summary>
    /// The Shipper Persona's Transport Orders as Norvia answers them through the Partner API, newest collection first.
    /// A failure is an answer, so a screen can keep what it last showed.
    /// </summary>
    Task<TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>> ListTrackedTransportOrdersAsync(
        PersonaId personaId, ShipperTrackingFilter filter, CancellationToken cancellationToken);

    /// <summary>One of the Shipper Persona's Transport Orders by Norvia's number, with its progress and Proof of delivery.</summary>
    Task<TrackingAnswer<ShipperTrackingDetail>> GetTrackedTransportOrderAsync(PersonaId personaId, string transportOrderNumber, CancellationToken cancellationToken);

    /// <summary>One of the Shipper Persona's Transport Orders by the Shipper's own reference.</summary>
    Task<TrackingAnswer<ShipperTrackingDetail>> FindTrackedTransportOrderAsync(PersonaId personaId, string shipperReference, CancellationToken cancellationToken);

    /// <summary>A file of the Transport Order's current Proof of delivery, as Norvia streams it.</summary>
    Task<TrackingAnswer<ShipperProofFileContent>> DownloadProofOfDeliveryFileAsync(
        PersonaId personaId, string transportOrderNumber, Guid fileId, CancellationToken cancellationToken);

    /// <summary>Every fault the applications own, with whether it is on and when it expires, for the presenter panel.</summary>
    Task<IReadOnlyList<FaultView>> ListFaultsAsync(CancellationToken cancellationToken);

    /// <summary>Switches a fault on for <paramref name="duration"/>, or off when it is null, and returns how it now stands.</summary>
    Task<FaultView> SwitchFaultAsync(string owner, string name, TimeSpan? duration, CancellationToken cancellationToken);

    /// <summary>The seeded Drivers' sign-in details to the Driver app, for the presenter panel.</summary>
    Task<DriverSignInsView> ListDriverSignInsAsync(CancellationToken cancellationToken);
}
