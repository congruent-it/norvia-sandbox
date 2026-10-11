using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>The Canonical Model schemas and samples, read from the test assembly so the check runs without Azure.</summary>
internal static class CanonicalSchemas
{
    private static readonly Lazy<JsonSchema> TripDispatchedSchema = new(() =>
    {
        _ = Load("transport-order.v1.schema.json");
        return Load("trip-dispatched.v1.schema.json");
    });

    private static readonly Lazy<JsonSchema> TripNotDeliveredSchema = new(() => Load("trip-not-delivered.v1.schema.json"));

    private static readonly Lazy<JsonSchema> ShipmentTakenOffTripSchema = new(() => Load("shipment-taken-off-trip.v1.schema.json"));

    public static IEnumerable<string> ShipmentTakenOffTripSamples() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.StartsWith("CanonicalModel/samples/shipment-taken-off-trip/", StringComparison.Ordinal));

    public static EvaluationResults ValidateShipmentTakenOffTrip(JsonNode body) =>
        ShipmentTakenOffTripSchema.Value.Evaluate(
            JsonSerializer.SerializeToElement(body),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static IEnumerable<string> TripNotDeliveredSamples() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.StartsWith("CanonicalModel/samples/trip-not-delivered/", StringComparison.Ordinal));

    public static EvaluationResults ValidateTripNotDelivered(JsonNode body) =>
        TripNotDeliveredSchema.Value.Evaluate(
            JsonSerializer.SerializeToElement(body),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static IEnumerable<string> TripDispatchedSamples() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.StartsWith("CanonicalModel/samples/trip-dispatched/", StringComparison.Ordinal));

    public static JsonNode ReadSample(string resource) => JsonNode.Parse(Read(resource))!;

    public static EvaluationResults ValidateTripDispatched(JsonNode body) =>
        TripDispatchedSchema.Value.Evaluate(
            JsonSerializer.SerializeToElement(body),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);

    private static string Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonSchema Load(string fileName)
    {
        var schema = JsonSchema.FromText(Read($"CanonicalModel/{fileName}"));
        SchemaRegistry.Global.Register(schema);
        return schema;
    }
}
