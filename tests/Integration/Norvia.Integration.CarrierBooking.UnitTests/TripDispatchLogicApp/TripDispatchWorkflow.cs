using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.TripDispatchLogicApp;

/// <summary>The P11 Trip dispatch Logic App's files, src/Integration/TripDispatchLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class TripDispatchWorkflow
{
    public const string Flow = "trip-dispatch";
    public const string Tracking = "Track_the_trip";
    public const string Check = "Check_the_Trip_version";
    public const string Complete = "Complete_the_message";
    public const string Credential = "Get_the_Driver_app_endpoint_credential";
    public const string Send = "Send_to_the_Driver_app";
    public const string Report = "Report_the_outcome_to_the_TMS";
    public const string ReportDelivered = "Report_the_version_delivered";
    public const string ReportNotDelivered = "Report_the_version_not_delivered";
    public const string DeadLetter = "Dead_letter_the_unreadable_trip";
    public const string FailTheRun = "Fail_the_run_on_the_unreadable_trip";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("TripDispatchLogicApp/trip-dispatch/workflow.json"));

    public static string WorkflowText => Contracts.Read("TripDispatchLogicApp/trip-dispatch/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("TripDispatchLogicApp/trip-dispatch/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("TripDispatchLogicApp/host.json");

    public static JsonObject ParametersFile => Read("TripDispatchLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("TripDispatchLogicApp/connections.json");

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
