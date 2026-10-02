using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests;

/// <summary>The files embedded in the test assembly under their path, such as <c>PartnerApi/booking.v1.schema.json</c>.</summary>
internal static class Contracts
{
    private static readonly Lazy<JsonSchema> CarrierAnswerSchema = new(() => JsonSchema.FromText(Read("PartnerApi/carrier-answer.v1.schema.json")));
    private static readonly Lazy<JsonSchema> BookingSchema = new(() => JsonSchema.FromText(Read("PartnerApi/booking.v1.schema.json")));

    public static string Read(string path)
    {
        using var stream = typeof(Contracts).Assembly.GetManifestResourceStream(path) ?? throw new InvalidOperationException($"{path} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>What breaks the Norvia Partner API's booking schema, or nothing.</summary>
    public static IEnumerable<string> BookingSchemaErrors(JsonNode? booking) => Errors(BookingSchema.Value, booking);

    /// <summary>What breaks the Norvia Partner API's Carrier answer schema, or nothing.</summary>
    public static IEnumerable<string> CarrierAnswerSchemaErrors(JsonNode? answer) => Errors(CarrierAnswerSchema.Value, answer);

    private static IEnumerable<string> Errors(JsonSchema schema, JsonNode? instance)
    {
        var results = schema.Evaluate(
            JsonSerializer.SerializeToElement(instance),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        return results.IsValid
            ? []
            : results.Details?.Where(detail => detail.Errors is not null).SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")) ?? [];
    }
}
