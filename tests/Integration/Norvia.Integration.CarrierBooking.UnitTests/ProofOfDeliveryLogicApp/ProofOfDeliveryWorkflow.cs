using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ProofOfDeliveryLogicApp;

/// <summary>The Carrier Proof of delivery Logic App's files, src/Integration/ProofOfDeliveryLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class ProofOfDeliveryWorkflow
{
    public const string Flow = "carrier-proof-of-delivery";
    public const string Tracking = "Track_the_message";
    public const string Map = "Map_the_proof_of_delivery";
    public const string TmsCall = "Record_in_the_TMS";
    public const string Settle = "Settle_the_message";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("ProofOfDeliveryLogicApp/carrier-proof-of-delivery/workflow.json"));

    public static string WorkflowText => Contracts.Read("ProofOfDeliveryLogicApp/carrier-proof-of-delivery/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("ProofOfDeliveryLogicApp/carrier-proof-of-delivery/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("ProofOfDeliveryLogicApp/host.json");

    public static JsonObject ParametersFile => Read("ProofOfDeliveryLogicApp/parameters.json");

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
