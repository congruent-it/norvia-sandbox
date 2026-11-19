using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ConsigneeNotificationLogicApp;

/// <summary>The P7 Consignee notification Logic App's files, src/Integration/ConsigneeNotificationLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class ConsigneeNotificationWorkflow
{
    public const string Flow = "consignee-notification";
    public const string Tracking = "Track_the_notification";
    public const string ReadAction = "Read_the_notification";
    public const string Complete = "Complete_the_message";
    public const string GetContent = "Get_the_Consignee_address_and_link";
    public const string TmsAnswer = "Read_the_TMS_answer";
    public const string TrackTmsAnswer = "Track_the_TMS_answer";
    public const string SendIfAny = "Send_the_email_if_there_is_one_to_send";
    public const string MapEmail = "Map_the_email";
    public const string Send = "Send_the_email";
    public const string TrackEmail = "Track_the_email";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("ConsigneeNotificationLogicApp/consignee-notification/workflow.json"));

    public static string WorkflowText => Contracts.Read("ConsigneeNotificationLogicApp/consignee-notification/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("ConsigneeNotificationLogicApp/consignee-notification/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("ConsigneeNotificationLogicApp/host.json");

    public static JsonObject ParametersFile => Read("ConsigneeNotificationLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("ConsigneeNotificationLogicApp/connections.json");

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
