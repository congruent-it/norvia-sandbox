using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Runs;

/// <summary>
/// Reads a Logic App run as it is now from the Logic Apps Standard management API, as the console's own identity (the
/// credential), never with the operator's token (ADR 0017). The estate grants that identity Logic Apps Standard Reader on
/// each Logic App, so it can read run history and nothing more: it never resubmits a run. A step's bodies are fetched
/// from run history's signed content links only when asked for, and never logged.
/// </summary>
internal sealed class LogicAppsWorkflowRuns(IHttpClientFactory clients, TokenCredential credential, IOptions<WorkflowRunsOptions> options)
    : IWorkflowRuns
{
    /// <summary>Calls the management API with the console's token.</summary>
    public const string ManagementClient = "logic-apps-management";

    /// <summary>Fetches bodies from run history's content links, which are signed and take no token. It logs nothing.</summary>
    public const string ContentsClient = "logic-apps-run-contents";

    private const string ApiVersion = "api-version=2018-11-01";
    private const string Management = "https://management.azure.com";
    private static readonly string[] ManagementScopes = [$"{Management}/.default"];

    public async Task<RecordedRun?> RunAsync(WorkflowRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var workflow = WorkflowAddress(run);
        var secure = await SecureDataAsync(workflow, cancellationToken);
        if (await GetAsync(RunAddress(workflow, run), cancellationToken) is not { } recorded)
        {
            return null;
        }

        var steps = new List<RecordedStep>();
        if (recorded["properties"]?["trigger"] is JsonObject trigger)
        {
            steps.Add(Step(trigger, trigger, secure));
        }

        // A run with many actions answers a page at a time.
        string? page = RunAddress(workflow, run, "/actions");
        while (page is not null && await GetAsync(page, cancellationToken) is { } actions)
        {
            steps.AddRange(Items(actions).Select(action => Step(action, action["properties"] as JsonObject, secure)));
            page = actions["nextLink"]?.GetValue<string>();
        }

        var properties = recorded["properties"];
        return new RecordedRun(Text(properties, "status") ?? "Unknown", Time(properties, "startTime"), Time(properties, "endTime"), steps);
    }

    public async Task<RecordedBodies?> BodiesAsync(WorkflowRun run, string stepName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var workflow = WorkflowAddress(run);
        var secure = (await SecureDataAsync(workflow, cancellationToken)).GetValueOrDefault(stepName, SecureData.None);
        if (await GetAsync(RunAddress(workflow, run), cancellationToken) is not { } recorded)
        {
            return null;
        }

        // The run is kept, so a step it doesn't have is a wrong address: that is an error, not an expired run.
        var step = recorded["properties"]?["trigger"] is JsonObject trigger && Text(trigger, "name") == stepName
            ? trigger
            : (await GetAsync(RunAddress(workflow, run, $"/actions/{Escaped(stepName)}"), cancellationToken)
                ?? throw new HttpRequestException($"The run has no step {stepName}.", null, HttpStatusCode.NotFound))["properties"];

        return new RecordedBodies(
            await BodyAsync(step?["inputsLink"], secure.Inputs, cancellationToken),
            await BodyAsync(step?["outputsLink"], secure.Outputs, cancellationToken));
    }

    private async Task<RecordedBody> BodyAsync(JsonNode? link, bool secure, CancellationToken cancellationToken)
    {
        if (secure)
        {
            return RecordedBody.Hidden;
        }

        if (Text(link, "uri") is not { } uri)
        {
            return RecordedBody.None;
        }

        // Run history can clear one body before the run itself; the other still shows.
        using var response = await clients.CreateClient(ContentsClient).GetAsync(new Uri(uri), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return RecordedBody.None;
        }

        response.EnsureSuccessStatusCode();
        return new RecordedBody(false, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Which steps keep their inputs or outputs out of run history, as the workflow's definition sets them. A workflow
    /// the Logic App doesn't have is a wrong address, so it fails rather than reading as a run past its retention. When
    /// the workflow comes without its definition no step is marked, and a hidden body shows as none.
    /// </summary>
    private async Task<Dictionary<string, SecureData>> SecureDataAsync(string workflow, CancellationToken cancellationToken)
    {
        var secure = new Dictionary<string, SecureData>(StringComparer.Ordinal);
        using var request = await RequestAsync($"{workflow}?{ApiVersion}", cancellationToken);
        using var response = await clients.CreateClient(ManagementClient).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["properties"]?["definition"] is not JsonObject definition)
        {
            return secure;
        }

        Collect(definition);
        return secure;

        // Steps nest in scopes, conditions, switches and loops, each under its name in the workflow, which is unique.
        void Collect(JsonObject node)
        {
            foreach (var (name, value) in node)
            {
                if (value is not JsonObject child)
                {
                    continue;
                }

                if (child["runtimeConfiguration"]?["secureData"]?["properties"] is JsonArray properties)
                {
                    var hidden = properties.Select(property => property?.GetValue<string>()).ToList();
                    secure[name] = new SecureData(hidden.Contains("inputs"), hidden.Contains("outputs"));
                }

                Collect(child);
            }
        }
    }

    // Null when run history no longer has what the address names.
    private async Task<JsonNode?> GetAsync(string address, CancellationToken cancellationToken)
    {
        using var request = await RequestAsync(address, cancellationToken);
        using var response = await clients.CreateClient(ManagementClient).SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task<HttpRequestMessage> RequestAsync(string address, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(ManagementScopes), cancellationToken);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private string WorkflowAddress(WorkflowRun run)
    {
        var resourceGroupId = !string.IsNullOrWhiteSpace(options.Value.ResourceGroupId)
            ? options.Value.ResourceGroupId.TrimEnd('/')
            : throw new InvalidOperationException(
                $"The console isn't connected to an estate: {WorkflowRunsOptions.SectionName}:{nameof(WorkflowRunsOptions.ResourceGroupId)} is not set.");

        return $"{Management}{resourceGroupId}/providers/Microsoft.Web/sites/{Escaped(run.LogicApp)}" +
            $"/hostruntime/runtime/webhooks/workflow/api/management/workflows/{Escaped(run.Workflow)}";
    }

    private static string RunAddress(string workflow, WorkflowRun run, string below = "") =>
        $"{workflow}/runs/{Escaped(run.RunId)}{below}?{ApiVersion}";

    private static RecordedStep Step(JsonNode named, JsonNode? properties, Dictionary<string, SecureData> secure)
    {
        var name = Text(named, "name") ?? string.Empty;
        var error = properties?["error"] is JsonObject failed && Text(failed, "code") is { } code
            ? new StepError(code, Text(failed, "message"))
            : null;
        var (inputs, outputs) = secure.GetValueOrDefault(name, SecureData.None);
        return new RecordedStep(
            name, Text(properties, "status") ?? "Unknown", Time(properties, "startTime"), Time(properties, "endTime"), error, inputs, outputs);
    }

    private static IEnumerable<JsonNode> Items(JsonNode page) => page["value"] is JsonArray items ? items.OfType<JsonNode>() : [];

    private static string? Text(JsonNode? node, string property) =>
        node?[property] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

    private static DateTimeOffset? Time(JsonNode? node, string property) =>
        DateTimeOffset.TryParse(Text(node, property), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;

    // The run's parts come from the operator's address bar, so none may reach another path of the API.
    private static string Escaped(string part) => Uri.EscapeDataString(part);
}
