using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>The JSON Schemas in contracts/, the Canonical Model's and the Norvia Partner API's.</summary>
internal sealed class Schemas
{
    private static readonly Lazy<Schemas> CanonicalTransportOrderSchema = new(() => Load("CanonicalModel", "transport-order.v1.schema.json"));
    private static readonly Lazy<Schemas> PartnerBookingSchema = new(() => Load("PartnerApi", "booking.v1.schema.json"));
    private static readonly Lazy<Schemas> PartnerBookingResponseSchema = new(() => Load("PartnerApi", "booking-response.v1.schema.json"));

    private readonly JsonSchema _schema;

    private Schemas(JsonSchema schema) => _schema = schema;

    public static Schemas CanonicalTransportOrder => CanonicalTransportOrderSchema.Value;

    public static Schemas PartnerBooking => PartnerBookingSchema.Value;

    public static Schemas PartnerBookingResponse => PartnerBookingResponseSchema.Value;

    public EvaluationResults Validate(JsonNode? instance) =>
        _schema.Evaluate(
            JsonSerializer.SerializeToElement(instance),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);

    private static Schemas Load(params string[] path) =>
        new(JsonSchema.FromFile(Path.Combine([AppContext.BaseDirectory, .. path])));
}
