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

    public static EvaluationResults ValidateShipmentPlanned(JsonNode body) => Validate(ShipmentPlannedSchema.Value, body);

    public static EvaluationResults ValidateShipmentReassigned(JsonNode body) => Validate(ShipmentReassignedSchema.Value, body);

    public static EvaluationResults ValidateShipmentReleased(JsonNode body) => Validate(ShipmentReleasedSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingConfirmed(JsonNode body) => Validate(CarrierBookingConfirmedSchema.Value, body);

    public static EvaluationResults ValidateCarrierBookingRejected(JsonNode body) => Validate(CarrierBookingRejectedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderAccepted(JsonNode body) => Validate(TransportOrderAcceptedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderRejected(JsonNode body) => Validate(TransportOrderRejectedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderRated(JsonNode body) => Validate(TransportOrderRatedSchema.Value, body);

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
