using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests.IntakeLogicApp;

/// <summary>The intake Logic App's workflow definition, src/Integration/IntakeLogicApp/transport-order-intake/workflow.json.</summary>
internal static class IntakeWorkflow
{
    public const string TmsCall = "Submit_to_the_TMS";

    public const string Tracking = "Track_the_booking";

    public const string CallerCheck = "Refuse_a_caller_other_than_API_Management";

    private static readonly Lazy<JsonObject> WorkflowFile = new(() => Read("IntakeLogicApp/transport-order-intake/workflow.json"));

    public static JsonObject Definition => WorkflowFile.Value["definition"]!.AsObject();

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

    public static JsonObject HostFile => Read("IntakeLogicApp/host.json");

    public static JsonObject ParametersFile => Read("IntakeLogicApp/parameters.json");

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
