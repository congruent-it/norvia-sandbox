using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

internal static class CanonicalModelSchemas
{
    private static readonly Lazy<JsonSchema> TransportOrderAcceptedSchema = new(() =>
    {
        Load("transport-order.v1.schema.json");
        return Load("transport-order-accepted.v1.schema.json");
    });

    private static readonly Lazy<JsonSchema> TransportOrderRejectedSchema = new(() => Load("transport-order-rejected.v1.schema.json"));

    public static EvaluationResults ValidateTransportOrderAccepted(JsonNode body) => Validate(TransportOrderAcceptedSchema.Value, body);

    public static EvaluationResults ValidateTransportOrderRejected(JsonNode body) => Validate(TransportOrderRejectedSchema.Value, body);

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
