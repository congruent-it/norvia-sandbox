using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

internal static class CanonicalModelSchemas
{
    // The events that carry a whole Transport Order refer to its schema, which the registry takes only once.
    private static readonly Lazy<JsonSchema> TransportOrderSchema = new(() => Load("transport-order.v1.schema.json"));

    private static readonly Lazy<JsonSchema> TransportOrderAcceptedSchema = new(() =>
    {
        _ = TransportOrderSchema.Value;
        return Load("transport-order-accepted.v1.schema.json");
    });

    private static readonly Lazy<JsonSchema> TransportOrderRejectedSchema = new(() => Load("transport-order-rejected.v1.schema.json"));

    private static readonly Lazy<JsonSchema> TransportOrderRatedSchema = new(() => Load("transport-order-rated.v1.schema.json"));

    private static readonly Lazy<JsonSchema> TransportOrderBillableSchema = new(() => Load("transport-order-billable.v1.schema.json"));

    private static readonly Lazy<JsonSchema> TransportOrderRatingFailedSchema = new(() => Load("transport-order-rating-failed.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentPlannedSchema = new(() => Load("shipment-planned.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentReassignedSchema = new(() => Load("shipment-reassigned.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentReleasedSchema = new(() =>
    {
        _ = TransportOrderSchema.Value;
        return Load("shipment-released.v1.schema.json");
    });

    private static readonly Lazy<JsonSchema> CarrierBookingConfirmedSchema = new(() => Load("carrier-booking-confirmed.v1.schema.json"));

    private static readonly Lazy<JsonSchema> CarrierBookingRejectedSchema = new(() => Load("carrier-booking-rejected.v1.schema.json"));

    private static readonly Lazy<JsonSchema> CarrierBookingNotDeliveredSchema = new(() => Load("carrier-booking-not-delivered.v1.schema.json"));

    private static readonly Lazy<JsonSchema> CarrierBookingWithdrawnSchema = new(() => Load("carrier-booking-withdrawn.v1.schema.json"));

    private static readonly Lazy<JsonSchema> TrackingEventSchema = new(() => Load("tracking-event.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentCollectedSchema = new(() => Load("shipment-collected.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentInTransitSchema = new(() => Load("shipment-in-transit.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentDeliveredSchema = new(() => Load("shipment-delivered.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentExceptionReportedSchema = new(() => Load("shipment-exception-reported.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ProofOfDeliveryReceivedSchema = new(() => Load("proof-of-delivery-received.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ExceptionOpenedSchema = new(() => Load("exception-opened.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ExceptionResolvedSchema = new(() => Load("exception-resolved.v1.schema.json"));

    private static readonly Lazy<JsonSchema> CarrierBookingCancelledSchema = new(() => Load("carrier-booking-cancelled.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipperTransportOrderListItemSchema = new(() => Load("shipper-transport-order-list-item.v1.schema.json"));

    public static EvaluationResults ValidateShipperTransportOrderListItem(JsonNode body) => Validate(ShipperTransportOrderListItemSchema.Value, body);

    private static readonly Lazy<JsonSchema> ShipperTransportOrderDetailSchema = new(() => Load("shipper-transport-order-detail.v1.schema.json"));

    public static EvaluationResults ValidateShipperTransportOrderDetail(JsonNode body) => Validate(ShipperTransportOrderDetailSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingCancelled(JsonNode body) => Validate(CarrierBookingCancelledSchema.Value, body);

    private static readonly Lazy<JsonSchema> CarrierCancellationNotDeliveredSchema = new(() => Load("carrier-cancellation-not-delivered.v1.schema.json"));

    public static EvaluationResults ValidateCarrierCancellationNotDelivered(JsonNode body) => Validate(CarrierCancellationNotDeliveredSchema.Value, body);

    private static readonly Lazy<JsonSchema> ShipperNotificationRequestedSchema = new(() => Load("shipper-notification-requested.v1.schema.json"));

    public static EvaluationResults ValidateShipperNotificationRequested(JsonNode body) => Validate(ShipperNotificationRequestedSchema.Value, body);

    private static readonly Lazy<JsonSchema> ShipperNotificationNotDeliveredSchema = new(() => Load("shipper-notification-not-delivered.v1.schema.json"));

    public static EvaluationResults ValidateShipperNotificationNotDelivered(JsonNode body) => Validate(ShipperNotificationNotDeliveredSchema.Value, body);

    private static readonly Lazy<JsonSchema> ShipmentDeliveryWindowAgreedSchema = new(() => Load("shipment-delivery-window-agreed.v1.schema.json"));

    public static EvaluationResults ValidateShipmentDeliveryWindowAgreed(JsonNode body) => Validate(ShipmentDeliveryWindowAgreedSchema.Value, body);

    public static EvaluationResults ValidateExceptionOpened(JsonNode body) => Validate(ExceptionOpenedSchema.Value, body);

    public static EvaluationResults ValidateExceptionResolved(JsonNode body) => Validate(ExceptionResolvedSchema.Value, body);

    public static EvaluationResults ValidateProofOfDeliveryReceived(JsonNode body) => Validate(ProofOfDeliveryReceivedSchema.Value, body);

    public static EvaluationResults ValidateTrackingEvent(JsonNode body) => Validate(TrackingEventSchema.Value, body);

    public static EvaluationResults ValidateShipmentCollected(JsonNode body) => Validate(ShipmentCollectedSchema.Value, body);

    public static EvaluationResults ValidateShipmentInTransit(JsonNode body) => Validate(ShipmentInTransitSchema.Value, body);

    public static EvaluationResults ValidateShipmentDelivered(JsonNode body) => Validate(ShipmentDeliveredSchema.Value, body);

    public static EvaluationResults ValidateShipmentExceptionReported(JsonNode body) => Validate(ShipmentExceptionReportedSchema.Value, body);

    public static EvaluationResults ValidateShipmentPlanned(JsonNode body) => Validate(ShipmentPlannedSchema.Value, body);

    public static EvaluationResults ValidateShipmentReassigned(JsonNode body) => Validate(ShipmentReassignedSchema.Value, body);

    public static EvaluationResults ValidateShipmentReleased(JsonNode body) => Validate(ShipmentReleasedSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingConfirmed(JsonNode body) => Validate(CarrierBookingConfirmedSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingRejected(JsonNode body) => Validate(CarrierBookingRejectedSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingNotDelivered(JsonNode body) => Validate(CarrierBookingNotDeliveredSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingWithdrawn(JsonNode body) => Validate(CarrierBookingWithdrawnSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderAccepted(JsonNode body) => Validate(TransportOrderAcceptedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderRejected(JsonNode body) => Validate(TransportOrderRejectedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderRated(JsonNode body) => Validate(TransportOrderRatedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderBillable(JsonNode body) => Validate(TransportOrderBillableSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderRatingFailed(JsonNode body) => Validate(TransportOrderRatingFailedSchema.Value, body);

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);

    private static EvaluationResults Validate(JsonSchema schema, JsonNode body) =>
        schema.Evaluate(
            JsonSerializer.SerializeToElement(body),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    private static JsonSchema Load(string fileName)
    {
        var schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "CanonicalModel", fileName));
        SchemaRegistry.Global.Register(schema);
        return schema;
    }
}
