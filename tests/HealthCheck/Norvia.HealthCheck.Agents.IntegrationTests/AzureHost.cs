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
    public const string TmsAddress = "http://tms-api";

    /// <summary>What the app takes as the time: 13 November 2026, ten in the morning UTC. Only the clock reading is fixed; timers run for real.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly AppFactory _app;

    /// <param name="timeoutSeconds">How long the app lets its Agent answer.</param>
    /// <param name="operatorRoles">The roles the local operator holds; all three operator roles unless a test shows what one without a role is refused.</param>
    public AzureHost(int timeoutSeconds = 5, string[]? operatorRoles = null)
    {
        _app = new AppFactory(this, timeoutSeconds, operatorRoles ?? ["HealthCheck.Run", "HealthCheck.Read", "HealthCheck.Cost"]);
    }

    public FakeAzure Azure { get; } = new();

    public FakeFoundry Foundry { get; } = new();

    public FakeTms Tms { get; } = new();

    /// <summary>The scopes the app asked its identity for a token with, in order.</summary>
    public IReadOnlyList<string> TokenScopes => [.. _scopes];

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _scopes = new();

    public IServiceProvider AppServices => _app.Services;

    public HttpClient AppClient() => _app.CreateClient();

    public ValueTask DisposeAsync()
    {
        Azure.Dispose();
        Foundry.Dispose();
        Tms.Dispose();
        return _app.DisposeAsync();
    }

    private sealed class AppFactory(AzureHost host, int timeoutSeconds, string[] operatorRoles) : WebApplicationFactory<AppProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["LocalOperator:Name"] = "Iris Operator",
                ["Estate:ResourceGroupId"] = ResourceGroupId,
                ["Estate:ServiceBusNamespace"] = Namespace,
                ["TmsApi:BaseAddress"] = TmsAddress,
                ["TmsApi:Scope"] = "api://norvia-tms/.default",
                ["Agents:Enabled"] = "true",
                ["Agents:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Agents:Foundry:ProjectEndpoint"] = Project,
                ["Agents:Api:Issuer"] = "https://norvia.local/tokens",
                ["Agents:Api:Audience"] = "api://norvia-health-check-agent-api",
                ["Agents:Api:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            }.Concat(operatorRoles.Select((role, index) => KeyValuePair.Create($"LocalOperator:Roles:{index}", role))))
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureTestServices(services =>
            {
                // The app's own managed identity, which holds Reader on the resource group and asks the Agent.
                services.AddSingleton<TokenCredential>(new FixedCredential(host._scopes));
                services.AddSingleton<TimeProvider>(new FixedClock());
                // The typed client of the cost view's Transport Order volume, named after its port; its far end is the TMS API.
                services.AddHttpClient("ITransportOrderVolume").ConfigurePrimaryHttpMessageHandler(() => host.Tms);
                services.AddHttpClient("estate-management").ConfigurePrimaryHttpMessageHandler(() => host.Azure);
                // The typed client of the app's Agent, named after its port; its far end is the project.
                services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => host.Foundry);
            });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedCredential(System.Collections.Concurrent.ConcurrentQueue<string> scopes) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            foreach (var scope in requestContext.Scopes)
            {
                scopes.Enqueue(scope);
            }

            return new("app-token", DateTimeOffset.MaxValue);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(GetToken(requestContext, cancellationToken));
    }
}

internal sealed record CostRow(DateOnly Date, string ResourceId, decimal Cost, string Currency = "EUR");

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
    private CostRow[][] _costPages = [[]];
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

    /// <summary>The rows Cost Management answers its query with, in one page.</summary>
    public FakeAzure Cost(params CostRow[] rows) => CostPages(rows);

    /// <summary>The rows Cost Management answers with, a page at a time, each but the last naming the next in nextLink.</summary>
    public FakeAzure CostPages(params CostRow[][] pages)
    {
        _costPages = pages;
        return this;
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

        if (address.Contains("/providers/Microsoft.CostManagement/query?", StringComparison.Ordinal))
        {
            return Json(CostPage(address));
        }

        if (address.Contains("ResourceGraph", StringComparison.Ordinal))
        {
            // Only the metric alerts are set; every other query over the estate's resources finds nothing, so those facts are unknown.
            var alerts = body.Contains("microsoft.insights/metricalerts", StringComparison.Ordinal) ? _alerts : [];
            return Json(new JsonObject { ["data"] = new JsonArray(alerts.Select(alert => (JsonNode?)alert.DeepClone()).ToArray()) });
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

    private static readonly string[] CostColumns = ["Cost", "UsageDate", "ResourceId", "Currency"];

    private JsonObject CostPage(string address)
    {
        var marker = "page=";
        var index = address.IndexOf(marker, StringComparison.Ordinal);
        var page = index < 0 ? 1 : int.Parse(address[(index + marker.Length)..], System.Globalization.CultureInfo.InvariantCulture);
        var properties = new JsonObject
        {
            ["columns"] = new JsonArray(CostColumns.Select(name => (JsonNode?)new JsonObject { ["name"] = name }).ToArray()),
            ["rows"] = new JsonArray(_costPages[page - 1].Select(row => (JsonNode?)new JsonArray(
                row.Cost, int.Parse(row.Date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture), row.ResourceId, row.Currency)).ToArray()),
        };
        if (page < _costPages.Length)
        {
            properties["nextLink"] = $"https://management.azure.com{AzureHost.ResourceGroupId}/providers/Microsoft.CostManagement/query?api-version=2023-11-01&page={page + 1}";
        }

        return new JsonObject { ["properties"] = properties };
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

/// <summary>The TMS API's volume endpoint: the days a test sets, each with its count, and nothing else of the TMS.</summary>
internal sealed class FakeTms : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<RecordedRequest> _requests = [];
    private (DateOnly Date, int Count)[] _volume = [];
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

    public void Volume(params (DateOnly Date, int Count)[] days) => _volume = days;

    public void Fail(HttpStatusCode status) => _failure = status;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri.TrimEnd('/'), request.Headers.Authorization?.ToString(), string.Empty));
        }

        if (_failure is { } failure)
        {
            return Task.FromResult(new HttpResponseMessage(failure));
        }

        var days = new JsonArray(_volume.Select(day => (JsonNode?)new JsonObject { ["date"] = day.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["count"] = day.Count }).ToArray());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(days.ToJsonString(), Encoding.UTF8, "application/json") });
    }
}
