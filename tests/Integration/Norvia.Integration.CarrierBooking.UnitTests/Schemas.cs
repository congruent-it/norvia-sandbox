using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>
/// The Carrier booking format's, the Carrier answer format's and the Carrier answer response's JSON Schemas in
/// contracts/partner-api, and the Canonical Model's Carrier booking answer.
/// </summary>
internal sealed class Schemas
{
    private static readonly Lazy<Schemas> CarrierBookingSchema = new(() => Of("PartnerApi/carrier-booking.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierAnswerSchema = new(() => Of("PartnerApi/carrier-answer.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierAnswerResponseSchema = new(() => Of("PartnerApi/carrier-answer-response.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierTrackingSchema = new(() => Of("PartnerApi/carrier-tracking.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierProofOfDeliverySchema = new(() => Of("PartnerApi/carrier-proof-of-delivery.v1.schema.json"));
    private static readonly Lazy<Schemas> ProofOfDeliverySchema = new(() => Of("CanonicalModel/proof-of-delivery.v1.schema.json"));
    private static readonly Lazy<Schemas> TrackingEventSchema = new(() => Of("CanonicalModel/tracking-event.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierBookingAnswerSchema = new(() => Of("CanonicalModel/carrier-booking-answer.v1.schema.json"));

    private static readonly Lazy<Schemas> CarrierCancellationSchema = new(() => Of("PartnerApi/carrier-cancellation.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierBookingCancelledSchema = new(() => Of("CanonicalModel/carrier-booking-cancelled.v1.schema.json"));
    private static readonly Lazy<Schemas> CarrierBookingWithdrawnSchema = new(() => Of("CanonicalModel/carrier-booking-withdrawn.v1.schema.json"));
    private static readonly Lazy<Schemas> ShipperNotificationSchema = new(() => Of("PartnerApi/shipper-notification.v1.schema.json"));
    private static readonly Lazy<Schemas> ShipperNotificationRequestedSchema = new(() => Of("CanonicalModel/shipper-notification-requested.v1.schema.json"));

    private static readonly Lazy<Schemas> ShipperTrackingSchema = new(() => Of("PartnerApi/shipper-tracking.v1.schema.json"));
    private static readonly Lazy<Schemas> ShipperTrackingListItemSchema = new(() => Of("PartnerApi/shipper-tracking-list-item.v1.schema.json"));
    private static readonly Lazy<Schemas> ShipperTransportOrderDetailSchema = new(() => Of("CanonicalModel/shipper-transport-order-detail.v1.schema.json"));
    private static readonly Lazy<Schemas> ShipperTransportOrderListItemSchema = new(() => Of("CanonicalModel/shipper-transport-order-list-item.v1.schema.json"));

    private static readonly Lazy<Schemas> TransportOrderBillableSchema = new(() => Of("CanonicalModel/transport-order-billable.v1.schema.json"));

    private static readonly Lazy<Schemas> ShipperInvoiceSchema = new(() => Of("PartnerApi/shipper-invoice.v1.schema.json"));
    private static readonly Lazy<Schemas> InvoiceDocumentSchema = new(() => Of("CanonicalModel/shipper-invoice.v1.schema.json"));

    private readonly JsonSchema _schema;

    private Schemas(JsonSchema schema) => _schema = schema;

    public static Schemas CarrierBooking => CarrierBookingSchema.Value;

    public static Schemas CarrierAnswer => CarrierAnswerSchema.Value;

    public static Schemas CarrierAnswerResponse => CarrierAnswerResponseSchema.Value;

    public static Schemas CarrierTracking => CarrierTrackingSchema.Value;

    public static Schemas CarrierProofOfDelivery => CarrierProofOfDeliverySchema.Value;

    /// <summary>The Canonical Model's Proof of delivery metadata, as the TMS's Proof of delivery API takes it.</summary>
    public static Schemas ProofOfDelivery => ProofOfDeliverySchema.Value;

    /// <summary>The Canonical Model's Tracking event, as the TMS's Tracking event API takes it.</summary>
    public static Schemas TrackingEvent => TrackingEventSchema.Value;

    /// <summary>The Canonical Model's answer, as the TMS's Carrier booking API takes it.</summary>
    public static Schemas CarrierBookingAnswer => CarrierBookingAnswerSchema.Value;

    public static Schemas CarrierCancellation => CarrierCancellationSchema.Value;

    /// <summary>The Canonical Model's event the Carrier cancellation map reads for a Cancelled Carrier booking.</summary>
    public static Schemas CarrierBookingCancelled => CarrierBookingCancelledSchema.Value;

    /// <summary>The Canonical Model's event the Carrier cancellation map reads for a Withdrawn Carrier booking.</summary>
    public static Schemas CarrierBookingWithdrawn => CarrierBookingWithdrawnSchema.Value;

    public static Schemas ShipperNotification => ShipperNotificationSchema.Value;

    /// <summary>The Canonical Model's event the Shipper notification map reads.</summary>
    public static Schemas ShipperNotificationRequested => ShipperNotificationRequestedSchema.Value;

    /// <summary>The Shipper tracking format of one Transport Order, as a Shipper gets or finds it.</summary>
    public static Schemas ShipperTracking => ShipperTrackingSchema.Value;

    /// <summary>The Shipper tracking format of one Transport Order in a list.</summary>
    public static Schemas ShipperTrackingListItem => ShipperTrackingListItemSchema.Value;

    /// <summary>The Canonical Model's Shipper view of one Transport Order, as the TMS Shipper tracking API returns it.</summary>
    public static Schemas ShipperTransportOrderDetail => ShipperTransportOrderDetailSchema.Value;

    /// <summary>The Canonical Model's Shipper view of one Transport Order in a list.</summary>
    public static Schemas ShipperTransportOrderListItem => ShipperTransportOrderListItemSchema.Value;

    /// <summary>The Canonical Model's event the Billing Logic App hands to the ERP billing API as it is.</summary>
    public static Schemas TransportOrderBillable => TransportOrderBillableSchema.Value;

    /// <summary>The Shipper invoice format, as the Shipper's invoice endpoint in the Partner portal takes it.</summary>
    public static Schemas ShipperInvoice => ShipperInvoiceSchema.Value;

    /// <summary>The Canonical Model's Shipper invoice, the document <c>ShipperInvoiceIssued</c> carries.</summary>
    public static Schemas InvoiceDocument => InvoiceDocumentSchema.Value;

    public EvaluationResults Validate(JsonNode? instance) =>
        _schema.Evaluate(
            JsonSerializer.SerializeToElement(instance),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);

    private static Schemas Of(string path) => new(JsonSchema.FromText(Contracts.Read(path)));
}
