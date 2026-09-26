using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Runs;

/// <summary>
/// Stands in for the Logic Apps Standard management API and the run history's content links: answers each address the
/// test gives it with the JSON or body it gives, anything else with 404, and keeps the requests it got.
/// </summary>
public sealed class RunHistory : HttpMessageHandler
{
    public const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";
    public const string LogicApp = "logic-intake-abc123";
    public const string Workflow = "transport-order-intake";
    public const string RunId = "08584421797283920101CU00";

    private const string ApiVersion = "api-version=2018-11-01";
    private const string WorkflowAddress =
        $"https://management.azure.com{ResourceGroupId}/providers/Microsoft.Web/sites/{LogicApp}/hostruntime/runtime/webhooks/workflow/api/management/workflows/{Workflow}";

    private readonly Dictionary<string, (HttpStatusCode Status, string Content)> _answers = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public static string RunAddress => $"{WorkflowAddress}/runs/{RunId}?{ApiVersion}";

    public static string ActionsAddress => $"{WorkflowAddress}/runs/{RunId}/actions?{ApiVersion}";

    public static string ActionAddress(string action) => $"{WorkflowAddress}/runs/{RunId}/actions/{action}?{ApiVersion}";

    public static string DefinitionAddress => $"{WorkflowAddress}?{ApiVersion}";

    /// <summary>A content link, as run history hands them out: signed, on the Logic App's own host.</summary>
    public static string ContentLink(string step, string content) =>
        $"https://{LogicApp}.azurewebsites.net:443/runtime/webhooks/workflow/scaleUnits/prod-00/workflows/abc/runs/{RunId}/contents/{step}{content}?api-version=2018-11-01&se=2026-09-26T12%3A00%3A00Z&sp=%2Fruns%2F&sv=1.0&sig=signature-{step}";

    public RunHistory Answer(string address, JsonNode json) => Answer(address, HttpStatusCode.OK, json.ToJsonString());

    public RunHistory Answer(string address, HttpStatusCode status, string content = "")
    {
        // As the client sends it: without the default port, escaped as a URI escapes it.
        _answers[new Uri(address).AbsoluteUri] = (status, content);
        return this;
    }

    /// <summary>The run, with its trigger's links when given.</summary>
    public RunHistory Run(string status, string start, string? end, JsonObject trigger) =>
        Answer(RunAddress, new JsonObject
        {
            ["name"] = RunId,
            ["properties"] = new JsonObject { ["status"] = status, ["startTime"] = start, ["endTime"] = end, ["trigger"] = trigger },
        });

    /// <summary>The workflow's definition, from its trigger and actions.</summary>
    public RunHistory Definition(JsonObject triggers, JsonObject actions) =>
        Answer(DefinitionAddress, new JsonObject
        {
            ["name"] = Workflow,
            ["properties"] = new JsonObject { ["definition"] = new JsonObject { ["triggers"] = triggers, ["actions"] = actions } },
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var (status, content) = _answers.TryGetValue(request.RequestUri!.AbsoluteUri, out var answer)
            ? answer
            : (HttpStatusCode.NotFound, """{"error":{"code":"WorkflowRunNotFound","message":"The workflow run was not found."}}""");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
    }
}
