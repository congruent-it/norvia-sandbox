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
    : IWorkflowRuns, IFailedRuns
{
    /// <summary>Calls the management API with the console's token.</summary>
    public const string ManagementClient = "logic-apps-management";

    /// <summary>Fetches bodies from run history's content links, which are signed and take no token. It logs nothing.</summary>
    public const string ContentsClient = "logic-apps-run-contents";

    private const string ApiVersion = "api-version=2018-11-01";
    private const string SitesApiVersion = "2023-12-01";
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

    public async Task<IReadOnlyList<RecordedFailedRun>> FailedRunsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var resourceGroup = ResourceGroup();
        var workflows = new List<WorkflowRun>();
        var apps = await ListAsync($"{Management}{resourceGroup}/providers/Microsoft.Web/sites?api-version={SitesApiVersion}", cancellationToken);
        foreach (var app in apps.Where(IsLogicApp).Select(site => Text(site, "name")).OfType<string>())
        {
            var found = await ListAsync($"{AppAddress(resourceGroup, app)}/workflows?{ApiVersion}", cancellationToken);
            workflows.AddRange(found.Select(workflow => Text(workflow, "name")).OfType<string>().Select(name => new WorkflowRun(app, name, string.Empty)));
        }

        // One workflow's failed runs don't wait for another's: the page refreshes every few seconds.
        var failed = await Task.WhenAll(workflows.Select(workflow => FailedRunsOfAsync(resourceGroup, workflow, since, cancellationToken)));
        return [.. failed.SelectMany(runs => runs)];
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

    private async Task<List<RecordedFailedRun>> FailedRunsOfAsync(
        string resourceGroup, WorkflowRun workflow, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var address = $"{AppAddress(resourceGroup, workflow.LogicApp)}/workflows/{Escaped(workflow.Workflow)}";
        var filter = Uri.EscapeDataString($"status eq 'Failed' and startTime ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}");
        var failed = new List<RecordedFailedRun>();
        foreach (var run in await ListAsync($"{address}/runs?{ApiVersion}&$filter={filter}", cancellationToken))
        {
            if (Text(run, "name") is not { } runId || Time(run["properties"], "startTime") is not { } start)
            {
                continue;
            }

            var steps = new List<(RecordedStep Step, JsonNode? Properties)>();
            foreach (var action in await ListAsync($"{address}/runs/{Escaped(runId)}/actions?{ApiVersion}", cancellationToken))
            {
                steps.Add((Step(action, action["properties"] as JsonObject, []), action["properties"]));
            }

            // The first step to end in failure is the cause; a scope around it fails after it.
            var cause = steps.Where(step => step.Step.Status is "Failed" or "TimedOut")
                .OrderBy(step => step.Step.End ?? DateTimeOffset.MaxValue)
                .Select(step => step.Step)
                .FirstOrDefault();
            failed.Add(new RecordedFailedRun(
                new WorkflowRun(workflow.LogicApp, workflow.Workflow, runId),
                start,
                Tracked(steps, "flow"),
                Tracked(steps, "correlationId"),
                cause?.Name,
                cause?.Error));
        }

        return failed;
    }

    // What a step tracked under a key of the tracking contract: any step may have, and the first to have is enough.
    private static string? Tracked(IEnumerable<(RecordedStep Step, JsonNode? Properties)> steps, string key) =>
        steps.Select(step => Text(step.Properties?["trackedProperties"], key)).FirstOrDefault(value => value is not null);

    // Every item of a list the API answers a page at a time, as a bare array or in a value.
    private async Task<List<JsonNode>> ListAsync(string address, CancellationToken cancellationToken)
    {
        var items = new List<JsonNode>();
        string? page = address;
        while (page is not null)
        {
            var answer = await GetAsync(page, cancellationToken);
            items.AddRange(answer is JsonArray array ? array.OfType<JsonNode>() : answer is null ? [] : Items(answer));
            page = answer is JsonObject paged ? paged["nextLink"]?.GetValue<string>() : null;
        }

        return items;
    }

    private static bool IsLogicApp(JsonNode site) => Text(site, "kind")?.Contains("workflowapp", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<HttpRequestMessage> RequestAsync(string address, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(ManagementScopes), cancellationToken);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private string WorkflowAddress(WorkflowRun run) => $"{AppAddress(ResourceGroup(), run.LogicApp)}/workflows/{Escaped(run.Workflow)}";

    private string ResourceGroup() =>
        !string.IsNullOrWhiteSpace(options.Value.ResourceGroupId)
            ? options.Value.ResourceGroupId.TrimEnd('/')
            : throw new InvalidOperationException(
                $"The console isn't connected to an estate: {WorkflowRunsOptions.SectionName}:{nameof(WorkflowRunsOptions.ResourceGroupId)} is not set.");

    private static string AppAddress(string resourceGroupId, string logicApp) =>
        $"{Management}{resourceGroupId}/providers/Microsoft.Web/sites/{Escaped(logicApp)}/hostruntime/runtime/webhooks/workflow/api/management";

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
