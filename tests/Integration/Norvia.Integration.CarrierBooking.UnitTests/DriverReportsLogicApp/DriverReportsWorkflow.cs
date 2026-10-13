using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.DriverReportsLogicApp;

/// <summary>The P12 Driver reports Logic App's files, src/Integration/DriverReportsLogicApp: its workflow, parameters, connections and host settings.</summary>
internal static class DriverReportsWorkflow
{
    public const string Flow = "trip-execution";
    public const string Tracking = "Track_the_report";
    public const string Check = "Check_the_report";
    public const string BuildTripStart = "Build_the_Trip_start";
    public const string BuildTrackingEvent = "Build_the_Tracking_event";
    public const string InitializeFileParts = "Initialize_the_file_parts";
    public const string FetchFiles = "Fetch_the_files";
    public const string ReadFile = "Read_the_file";
    public const string AppendFilePart = "Append_the_file_part";
    public const string DescribeFiles = "Select_the_file_descriptions";
    public const string BuildProofOfDelivery = "Build_the_Proof_of_delivery";
    public const string BuildMultipart = "Build_the_Proof_of_delivery_multipart";
    public const string DeadLetterFilesGone = "Dead_letter_the_report_whose_files_are_gone";
    public const string ReportFilesGone = "Report_the_files_are_gone";
    public const string FailTheRunOnFilesGone = "Fail_the_run_on_the_files_that_are_gone";
    public const string DeleteFiles = "Delete_the_files";
    public const string DeleteFile = "Delete_the_file";
    public const string SendToTms = "Send_the_report_to_the_TMS";
    public const string Settle = "Settle_the_report_and_tell_the_Driver_app";
    public const string Complete = "Complete_the_message";
    public const string ReportRecorded = "Report_recorded";
    public const string CheckUnavailable = "Check_whether_the_TMS_was_unavailable";
    public const string DeadLetterUnavailable = "Dead_letter_the_report_the_TMS_could_not_take";
    public const string ReportNotRecorded = "Report_not_recorded";
    public const string DeadLetterRefused = "Dead_letter_the_refused_report";
    public const string ReportRefused = "Report_refused";
    public const string DeadLetterUnreadable = "Dead_letter_the_unreadable_report";
    public const string FailTheRun = "Fail_the_run_on_the_unreadable_report";

    private static readonly Lazy<JsonObject> Parsed = new(() => Read("DriverReportsLogicApp/driver-reports/workflow.json"));

    public static string WorkflowText => Contracts.Read("DriverReportsLogicApp/driver-reports/workflow.json");

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

    public static JsonObject WorkflowFile() => Read("DriverReportsLogicApp/driver-reports/workflow.json");

    public static string Kind => WorkflowFile()["kind"]!.GetValue<string>();

    public static JsonObject HostFile => Read("DriverReportsLogicApp/host.json");

    public static JsonObject ParametersFile => Read("DriverReportsLogicApp/parameters.json");

    public static JsonObject ConnectionsFile => Read("DriverReportsLogicApp/connections.json");

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
