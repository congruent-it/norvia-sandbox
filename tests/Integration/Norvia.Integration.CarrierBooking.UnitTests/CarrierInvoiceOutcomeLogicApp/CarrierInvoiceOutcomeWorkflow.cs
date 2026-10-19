using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierInvoiceOutcomeLogicApp;

/// <summary>The E2 Carrier invoice outcome Logic App's files, src/Integration/CarrierInvoiceOutcomeLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class CarrierInvoiceOutcomeWorkflow
{
    public const string Flow = "carrier-invoices";
    public const string Tracking = "Track_the_invoice";
    public const string MapAction = "Map_the_invoice";
    public const string Credential = "Get_the_Carrier_endpoint_credential";
    public const string Send = "Send_to_the_Carrier";
    public const string Report = "Report_the_outcome_to_the_ERP";
    public const string ReportTold = "Report_the_outcome_told";
    public const string ReportNotDelivered = "Report_the_outcome_not_delivered";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("CarrierInvoiceOutcomeLogicApp/carrier-invoice-outcome/workflow.json"));

    public static string WorkflowText => Contracts.Read("CarrierInvoiceOutcomeLogicApp/carrier-invoice-outcome/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("CarrierInvoiceOutcomeLogicApp/carrier-invoice-outcome/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("CarrierInvoiceOutcomeLogicApp/host.json");

    public static JsonObject ParametersFile => Read("CarrierInvoiceOutcomeLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("CarrierInvoiceOutcomeLogicApp/connections.json");

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
