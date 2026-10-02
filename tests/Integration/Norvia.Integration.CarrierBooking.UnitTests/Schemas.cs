using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The Carrier booking format's JSON Schema in contracts/partner-api, and the Canonical Model's <c>ShipmentReleased</c>.</summary>
internal sealed class Schemas
{
    private static readonly Lazy<Schemas> CarrierBookingSchema = new(() => new(JsonSchema.FromText(Contracts.Read("PartnerApi/carrier-booking.v1.schema.json"))));

    private readonly JsonSchema _schema;

    private Schemas(JsonSchema schema) => _schema = schema;

    public static Schemas CarrierBooking => CarrierBookingSchema.Value;

    public EvaluationResults Validate(JsonNode? instance) =>
        _schema.Evaluate(
            JsonSerializer.SerializeToElement(instance),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);
}
