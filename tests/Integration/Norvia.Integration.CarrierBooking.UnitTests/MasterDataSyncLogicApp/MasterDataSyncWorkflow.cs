using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.MasterDataSyncLogicApp;

/// <summary>The E3 master-data sync Logic App's files, src/Integration/MasterDataSyncLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class MasterDataSyncWorkflow
{
    public const string Flow = "master-data";
    public const string Tracking = "Track_the_master_data_record";
    public const string Check = "Check_the_master_data_record";
    public const string Replace = "Replace_the_record_in_the_TMS";
    public const string Settle = "Settle_the_message";
    public const string Complete = "Complete_the_message";
    public const string DeadLetterRefused = "Dead_letter_the_refused_record";
    public const string FailTheRefusedRun = "Fail_the_run_on_the_refused_record";
    public const string DeadLetterUnreadable = "Dead_letter_the_unreadable_record";
    public const string FailTheUnreadableRun = "Fail_the_run_on_the_unreadable_record";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("MasterDataSyncLogicApp/master-data-sync/workflow.json"));

    public static string WorkflowText => Contracts.Read("MasterDataSyncLogicApp/master-data-sync/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("MasterDataSyncLogicApp/master-data-sync/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("MasterDataSyncLogicApp/host.json");

    public static JsonObject ParametersFile => Read("MasterDataSyncLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("MasterDataSyncLogicApp/connections.json");

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
