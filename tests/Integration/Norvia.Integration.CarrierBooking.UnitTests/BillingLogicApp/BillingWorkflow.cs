using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.BillingLogicApp;

/// <summary>The E1 Billing Logic App's files, src/Integration/BillingLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class BillingWorkflow
{
    public const string Flow = "billing";
    public const string Tracking = "Track_the_billable_line";
    public const string Check = "Check_the_billable_line";
    public const string Record = "Record_the_billable_line";
    public const string Settle = "Settle_the_message";
    public const string Complete = "Complete_the_message";
    public const string DeadLetterRefused = "Dead_letter_the_refused_billable_line";
    public const string FailTheRefusedRun = "Fail_the_run_on_the_refused_billable_line";
    public const string DeadLetterUnreadable = "Dead_letter_the_unreadable_billable_line";
    public const string FailTheUnreadableRun = "Fail_the_run_on_the_unreadable_billable_line";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("BillingLogicApp/billing/workflow.json"));

    public static string WorkflowText => Contracts.Read("BillingLogicApp/billing/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("BillingLogicApp/billing/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("BillingLogicApp/host.json");

    public static JsonObject ParametersFile => Read("BillingLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("BillingLogicApp/connections.json");

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
