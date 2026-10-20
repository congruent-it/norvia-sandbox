using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Simulator.Infrastructure.UnitTests;

/// <summary>The files embedded in the test assembly under their path, such as <c>PartnerApi/booking.v1.schema.json</c>.</summary>
internal static class Contracts
{
    private static readonly Lazy<JsonSchema> BookingSchema = new(() => Schema("booking"));
    private static readonly Lazy<JsonSchema> CarrierAnswerSchema = new(() => Schema("carrier-answer"));
    private static readonly Lazy<JsonSchema> CarrierTrackingSchema = new(() => Schema("carrier-tracking"));
    private static readonly Lazy<JsonSchema> CarrierProofSchema = new(() => Schema("carrier-proof-of-delivery"));
    private static readonly Lazy<JsonSchema> CarrierInvoiceSchema = new(() => Schema("carrier-invoice"));

    public static string ReadText(string path)
    {
        using var stream = OpenRead(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Stream OpenRead(string path) =>
        typeof(Contracts).Assembly.GetManifestResourceStream(path) ?? throw new InvalidOperationException($"{path} is not embedded.");

    /// <summary>What breaks the Norvia Partner API's booking schema, or nothing.</summary>
    public static IEnumerable<string> BookingSchemaErrors(JsonNode? booking) => Errors(BookingSchema.Value, booking);

    /// <summary>What breaks the Carrier answer schema, or nothing.</summary>
    public static IEnumerable<string> CarrierAnswerSchemaErrors(JsonNode? answer) => Errors(CarrierAnswerSchema.Value, answer);

    /// <summary>What breaks the Carrier tracking schema, or nothing.</summary>
    public static IEnumerable<string> CarrierTrackingSchemaErrors(JsonNode? tracking) => Errors(CarrierTrackingSchema.Value, tracking);

    /// <summary>What breaks the Carrier Proof of delivery schema, or nothing.</summary>
    public static IEnumerable<string> CarrierProofSchemaErrors(JsonNode? proof) => Errors(CarrierProofSchema.Value, proof);

    /// <summary>What breaks the Carrier invoice schema, or nothing.</summary>
    public static IEnumerable<string> CarrierInvoiceSchemaErrors(JsonNode? invoice) => Errors(CarrierInvoiceSchema.Value, invoice);

    private static JsonSchema Schema(string name) => JsonSchema.FromText(ReadText($"PartnerApi/{name}.v1.schema.json"));

    private static IEnumerable<string> Errors(JsonSchema schema, JsonNode? document)
    {
        var results = schema.Evaluate(
            JsonSerializer.SerializeToElement(document),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        return results.IsValid
            ? []
            : results.Details?.Where(detail => detail.Errors is not null).SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")) ?? [];
    }
}
