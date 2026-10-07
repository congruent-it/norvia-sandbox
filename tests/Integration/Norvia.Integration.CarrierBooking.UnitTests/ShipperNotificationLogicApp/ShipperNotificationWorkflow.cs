using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ShipperNotificationLogicApp;

/// <summary>The P7 Shipper notification Logic App's files, src/Integration/ShipperNotificationLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class ShipperNotificationWorkflow
{
    public const string Flow = "shipper-notification";
    public const string Tracking = "Track_the_notification";
    public const string MapAction = "Map_the_notification";
    public const string Credential = "Get_the_Shipper_endpoint_credential";
    public const string Send = "Send_to_the_Shipper";
    public const string Report = "Report_the_outcome_to_the_TMS";
    public const string ReportSent = "Report_the_notification_sent";
    public const string ReportNotDelivered = "Report_the_notification_not_delivered";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("ShipperNotificationLogicApp/shipper-notification/workflow.json"));

    public static string WorkflowText => Contracts.Read("ShipperNotificationLogicApp/shipper-notification/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("ShipperNotificationLogicApp/shipper-notification/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("ShipperNotificationLogicApp/host.json");

    public static JsonObject ParametersFile => Read("ShipperNotificationLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("ShipperNotificationLogicApp/connections.json");

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
