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

    public static EvaluationResults ValidateTransportOrderAccepted(JsonNode body) =>
        TransportOrderAcceptedSchema.Value.Evaluate(
            JsonSerializer.SerializeToElement(body),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    private static JsonSchema Load(string fileName)
    {
        var schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "CanonicalModel", fileName));
        SchemaRegistry.Global.Register(schema);
        return schema;
    }
}
