using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Simulator.Infrastructure.UnitTests;

/// <summary>The files embedded in the test assembly under their path, such as <c>PartnerApi/booking.v1.schema.json</c>.</summary>
internal static class Contracts
{
    private static readonly Lazy<JsonSchema> BookingSchema = new(() => JsonSchema.FromText(ReadText("PartnerApi/booking.v1.schema.json")));

    public static string ReadText(string path)
    {
        using var stream = OpenRead(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Stream OpenRead(string path) =>
        typeof(Contracts).Assembly.GetManifestResourceStream(path) ?? throw new InvalidOperationException($"{path} is not embedded.");

    /// <summary>What breaks the Norvia Partner API's booking schema, or nothing.</summary>
    public static IEnumerable<string> BookingSchemaErrors(JsonNode? booking)
    {
        var results = BookingSchema.Value.Evaluate(
            JsonSerializer.SerializeToElement(booking),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        return results.IsValid
            ? []
            : results.Details?.Where(detail => detail.Errors is not null).SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")) ?? [];
    }
}
