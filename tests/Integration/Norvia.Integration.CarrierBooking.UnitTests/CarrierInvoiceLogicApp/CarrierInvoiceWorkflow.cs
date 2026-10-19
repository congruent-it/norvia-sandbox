using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierInvoiceLogicApp;

/// <summary>The Carrier invoice Logic App's files, src/Integration/CarrierInvoiceLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class CarrierInvoiceWorkflow
{
    public const string Flow = "carrier-invoice";
    public const string CallerCheck = "Refuse_a_caller_other_than_API_Management";
    public const string Tracking = "Track_the_invoice";
    public const string Map = "Map_the_invoice";
    public const string ErpCall = "Receive_at_the_ERP";
    public const string Relay = "Relay_the_ERP_outcome";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("CarrierInvoiceLogicApp/carrier-invoice/workflow.json"));

    public static string WorkflowText => Contracts.Read("CarrierInvoiceLogicApp/carrier-invoice/workflow.json");

    public static JsonObject Definition => Parsed.Value["definition"]!.AsObject();

    public static JsonObject Trigger => Definition["triggers"]!.AsObject().Single().Value!.AsObject();

    public static JsonObject Actions => Definition["actions"]!.AsObject();

    public static JsonObject Action(string name) =>
        Find(Actions, name) ?? throw new InvalidOperationException($"The workflow has no action {name}.");

    /// <summary>Every trigger and action in the workflow, nested ones included.</summary>
    public static IEnumerable<(string Name, JsonObject Operation)> Operations()
    {
        yield return ("trigger", Trigger);
        foreach (var action in AllActions(Actions))
        {
            yield return action;
        }
    }

    public static JsonObject WorkflowFile() => Read("CarrierInvoiceLogicApp/carrier-invoice/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("CarrierInvoiceLogicApp/host.json");

    public static JsonObject ParametersFile => Read("CarrierInvoiceLogicApp/parameters.json");

    private static IEnumerable<(string Name, JsonObject Operation)> AllActions(JsonObject actions)
    {
        foreach (var (name, node) in actions)
        {
            var action = node!.AsObject();
            yield return (name, action);
            foreach (var nested in new[] { action["actions"], action["else"]?["actions"] }.OfType<JsonObject>().SelectMany(AllActions))
            {
                yield return nested;
            }
        }
    }

    private static JsonObject? Find(JsonObject actions, string name) =>
        AllActions(actions).Where(action => action.Name == name).Select(action => action.Operation).FirstOrDefault();

    private static JsonObject Read(string path) =>
        JsonNode.Parse(Contracts.Read(path))?.AsObject() ?? throw new InvalidOperationException($"{path} holds no JSON object.");
}
