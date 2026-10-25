extern alias HealthCheckWeb;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using AppProgram = HealthCheckWeb::Program;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The Estate health check as it runs in Azure (#392): <c>Estate:ResourceGroupId</c> and <c>Agents:Foundry:ProjectEndpoint</c> set, so its estate is the
/// Azure adapter and its <c>IAgentClient</c> the Foundry client, both built by the app's own registrations. Their far ends are faked over real HTTP
/// pipelines: <see cref="Azure"/> answers for Resource Graph and the namespace's management plane, <see cref="Foundry"/> for the project's Responses
/// endpoint. Each test starts an app of its own.
/// </summary>
internal sealed class AzureHost : IAsyncDisposable
{
    public const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-00000000000a/resourceGroups/rg-norvia";
    public const string Namespace = "sb-norvia-abc";
    public const string NamespaceId = $"{ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/{Namespace}";
    public const string Project = "https://ais-norvia.services.ai.azure.com/api/projects/norvia";

    private readonly AppFactory _app;

    public AzureHost(int timeoutSeconds = 5)
    {
        _app = new AppFactory(this, timeoutSeconds);
    }

    public FakeAzure Azure { get; } = new();

    public FakeFoundry Foundry { get; } = new();

    public IServiceProvider AppServices => _app.Services;

    public HttpClient AppClient() => _app.CreateClient();

    public ValueTask DisposeAsync()
    {
        Azure.Dispose();
        Foundry.Dispose();
        return _app.DisposeAsync();
    }

    private sealed class AppFactory(AzureHost host, int timeoutSeconds) : WebApplicationFactory<AppProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["LocalOperator:Name"] = "Iris Operator",
                ["LocalOperator:Roles:0"] = "HealthCheck.Run",
                ["LocalOperator:Roles:1"] = "HealthCheck.Read",
                ["Estate:ResourceGroupId"] = ResourceGroupId,
                ["Estate:ServiceBusNamespace"] = Namespace,
                ["Agents:Enabled"] = "true",
                ["Agents:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Agents:Foundry:ProjectEndpoint"] = Project,
                ["Agents:Api:Issuer"] = "https://norvia.local/tokens",
                ["Agents:Api:Audience"] = "api://norvia-health-check-agent-api",
                ["Agents:Api:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            })
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureTestServices(services =>
            {
                // The app's own managed identity, which holds Reader on the resource group and asks the Agent.
                services.AddSingleton<TokenCredential>(new FixedCredential());
                services.AddHttpClient("estate-management").ConfigurePrimaryHttpMessageHandler(() => host.Azure);
                // The typed client of the app's Agent, named after its port; its far end is the project.
                services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => host.Foundry);
            });
        }
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("app-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(GetToken(requestContext, cancellationToken));
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);

/// <summary>Resource Graph and the namespace's management plane, answering from what the test sets. A test may change the answer between runs.</summary>
internal sealed class FakeAzure : HttpMessageHandler
{
    private const string Address = $"https://management.azure.com{AzureHost.NamespaceId}";

    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = [];
    private string[] _queues = [];
    private (string Topic, string Name)[] _subscriptions = [];
    private JsonObject[] _alerts = [];
    private HttpStatusCode? _failure;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public FakeAzure Entities(string[] queues, params (string Topic, string Name)[] subscriptions)
    {
        (_queues, _subscriptions) = (queues, subscriptions);
        return this;
    }

    /// <summary>The metric alert rules Resource Graph holds, replacing any before.</summary>
    public FakeAzure Alerts(params JsonObject[] alerts)
    {
        _alerts = alerts;
        return this;
    }

    public void Fail(HttpStatusCode status) => _failure = status;

    /// <summary>A metric alert rule on the namespace's dead-lettered messages of the entities named (of all of them, when none is).</summary>
    public static JsonObject DeadLetterAlert(string name, params string[] entities) => new()
    {
        ["name"] = name,
        ["enabled"] = true,
        ["scopes"] = new JsonArray(AzureHost.NamespaceId),
        ["criteria"] = new JsonObject
        {
            ["allOf"] = new JsonArray(new JsonObject
            {
                ["metricName"] = "DeadletteredMessages",
                ["dimensions"] = entities.Length == 0
                    ? new JsonArray()
                    : new JsonArray(new JsonObject
                    {
                        ["name"] = "EntityName",
                        ["operator"] = "Include",
                        ["values"] = new JsonArray(entities.Select(entity => (JsonNode?)JsonValue.Create(entity)).ToArray()),
                    }),
            }),
        },
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var address = request.RequestUri!.AbsoluteUri;
        lock (_lock)
        {
            _requests.Add(new RecordedRequest(request.Method, address, request.Headers.Authorization?.ToString(), body));
        }

        if (_failure is { } failure)
        {
            return new HttpResponseMessage(failure);
        }

        if (address.Contains("ResourceGraph", StringComparison.Ordinal))
        {
            return Json(new JsonObject { ["data"] = new JsonArray(_alerts.Select(alert => (JsonNode?)alert.DeepClone()).ToArray()) });
        }

        if (address.StartsWith(Address + "/queues?", StringComparison.Ordinal))
        {
            return Json(Items(_queues));
        }

        if (address.StartsWith(Address + "/topics?", StringComparison.Ordinal))
        {
            return Json(Items(_subscriptions.Select(subscription => subscription.Topic).Distinct()));
        }

        foreach (var topic in _subscriptions.Select(subscription => subscription.Topic).Distinct())
        {
            if (address.StartsWith($"{Address}/topics/{topic}/subscriptions?", StringComparison.Ordinal))
            {
                return Json(Items(_subscriptions.Where(subscription => subscription.Topic == topic).Select(subscription => subscription.Name)));
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static JsonObject Items(IEnumerable<string> names) =>
        new() { ["value"] = new JsonArray(names.Select(name => (JsonNode?)new JsonObject { ["name"] = name }).ToArray()) };

    private static HttpResponseMessage Json(JsonObject content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>The project's Responses endpoint: a POST is answered as the test says, a DELETE with 200.</summary>
internal sealed class FakeFoundry : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = [];
    private JsonObject[] _output = [];
    private HttpStatusCode? _failure;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public void Answer(params JsonObject[] output) => _output = output;

    public void Fail(HttpStatusCode status) => _failure = status;

    public static JsonObject McpCall(string tool, string output) => new()
    {
        ["type"] = "mcp_call",
        ["id"] = "mcp_1",
        ["server_label"] = "health-check",
        ["name"] = tool,
        ["arguments"] = "{}",
        ["output"] = output,
        ["error"] = null,
    };

    public static JsonObject Message(string text) => new()
    {
        ["type"] = "message",
        ["role"] = "assistant",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_lock)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
        }

        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (_failure is { } failure)
        {
            return new HttpResponseMessage(failure);
        }

        var response = new JsonObject
        {
            ["id"] = "resp-1",
            ["object"] = "response",
            ["status"] = "completed",
            ["output"] = new JsonArray(_output.Select(item => (JsonNode?)item.DeepClone()).ToArray()),
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
