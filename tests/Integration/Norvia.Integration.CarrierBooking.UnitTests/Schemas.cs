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
    private static readonly Lazy<Schemas> CarrierBookingAnswerSchema = new(() => Of("CanonicalModel/carrier-booking-answer.v1.schema.json"));

    private readonly JsonSchema _schema;

    private Schemas(JsonSchema schema) => _schema = schema;

    public static Schemas CarrierBooking => CarrierBookingSchema.Value;

    public static Schemas CarrierAnswer => CarrierAnswerSchema.Value;

    public static Schemas CarrierAnswerResponse => CarrierAnswerResponseSchema.Value;

    /// <summary>The Canonical Model's answer, as the TMS's Carrier booking API takes it.</summary>
    public static Schemas CarrierBookingAnswer => CarrierBookingAnswerSchema.Value;

    public EvaluationResults Validate(JsonNode? instance) =>
        _schema.Evaluate(
            JsonSerializer.SerializeToElement(instance),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);

    private static Schemas Of(string path) => new(JsonSchema.FromText(Contracts.Read(path)));
}
