using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierCostLogicApp;

/// <summary>The P9 Carrier cost Logic App's files, src/Integration/CarrierCostLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class CarrierCostWorkflow
{
    public const string Flow = "carrier-costs";
    public const string Tracking = "Track_the_carrier_cost";
    public const string Check = "Check_the_carrier_cost";
    public const string Record = "Record_the_carrier_cost";
    public const string Settle = "Settle_the_message";
    public const string Complete = "Complete_the_message";
    public const string DeadLetterRefused = "Dead_letter_the_refused_carrier_cost";
    public const string FailTheRefusedRun = "Fail_the_run_on_the_refused_carrier_cost";
    public const string DeadLetterUnreadable = "Dead_letter_the_unreadable_carrier_cost";
    public const string FailTheUnreadableRun = "Fail_the_run_on_the_unreadable_carrier_cost";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("CarrierCostLogicApp/carrier-cost/workflow.json"));

    public static string WorkflowText => Contracts.Read("CarrierCostLogicApp/carrier-cost/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("CarrierCostLogicApp/carrier-cost/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("CarrierCostLogicApp/host.json");

    public static JsonObject ParametersFile => Read("CarrierCostLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("CarrierCostLogicApp/connections.json");

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
