using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.AnswerLogicApp;

/// <summary>The P4 booking Logic App's files, src/Integration/AnswerLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class AnswerWorkflow
{
    public const string Flow = "carrier-answer";
    public const string CallerCheck = "Refuse_a_caller_other_than_API_Management";
    public const string Tracking = "Track_the_answer";
    public const string Map = "Map_the_answer";
    public const string TmsCall = "Answer_to_the_TMS";
    public const string Relay = "Relay_the_TMS_outcome";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("AnswerLogicApp/carrier-answer/workflow.json"));

    public static string WorkflowText => Contracts.Read("AnswerLogicApp/carrier-answer/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("AnswerLogicApp/carrier-answer/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("AnswerLogicApp/host.json");

    public static JsonObject ParametersFile => Read("AnswerLogicApp/parameters.json");

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
