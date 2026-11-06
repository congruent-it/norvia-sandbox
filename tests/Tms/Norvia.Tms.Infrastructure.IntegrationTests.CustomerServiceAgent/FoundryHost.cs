using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Infrastructure.Agents;

namespace Norvia.Tms.Infrastructure.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The TMS UI's Agent client as it runs in Azure (ADR 0040): <c>Agents:Enabled</c> and <c>Agents:Foundry:ProjectEndpoint</c> set, so its <see cref="IAgentClient"/> is
/// the Foundry client built by the TMS UI's own registration, over a real HTTP pipeline whose far end is <see cref="Responses"/>, the project's Responses endpoint.
/// The runs the client traces are collected in <see cref="Spans"/>. Each test builds a host of its own.
/// </summary>
internal sealed class FoundryHost : IAsyncDisposable
{
    public const string Project = "https://ais-norvia.services.ai.azure.com/api/projects/norvia";

    private readonly IHost _host;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];

    public FoundryHost(int timeoutSeconds = 5)
    {
        // A source of its own, so the runs of other tests running side by side are not counted as this one's.
        var source = $"Norvia.Test.TmsUi.{Guid.NewGuid():N}";
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { ApplicationName = source });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agents:Enabled"] = "true",
            ["Agents:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Agents:Foundry:ProjectEndpoint"] = Project,
        });
        builder.AddAgentClient();
        builder.Services.AddSingleton<TokenCredential>(new FixedCredential());
        // The typed client of the TMS UI's Agent, named after its port; its far end is the project.
        builder.Services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => Responses);
        _host = builder.Build();

        _listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (_spans)
                {
                    _spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public FakeResponses Responses { get; } = new();

    public IAgentClient Client => _host.Services.GetRequiredService<IAgentClient>();

    public IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_spans)
            {
                return [.. _spans];
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _listener.Dispose();
        _host.Dispose();
        Responses.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("foundry-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(GetToken(requestContext, cancellationToken));
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);

/// <summary>The project's Responses endpoint: a POST is answered as the test says, a DELETE with the status the test sets.</summary>
internal sealed class FakeResponses : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private JsonObject[] _output = [];
    private string _status = "completed";
    private HttpStatusCode? _failure;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.OK;

    public bool Hang { get; set; }

    public void Answer(JsonObject[] output, string status = "completed") => (_output, _status) = (output, status);

    public void Fail(HttpStatusCode status) => _failure = status;

    public static JsonObject McpCall(string tool, string? error = null) => new()
    {
        ["type"] = "mcp_call",
        ["id"] = "mcp_1",
        ["server_label"] = "customer-service",
        ["name"] = tool,
        ["arguments"] = "{}",
        ["output"] = "{}",
        ["error"] = error is null ? null : JsonNode.Parse(error),
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
        lock (_requests)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
        }

        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(DeleteStatus);
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (_failure is { } failure)
        {
            return new HttpResponseMessage(failure);
        }

        var response = new JsonObject
        {
            ["id"] = "resp-1",
            ["object"] = "response",
            ["status"] = _status,
            ["output"] = new JsonArray(_output.Select(item => (JsonNode?)item.DeepClone()).ToArray()),
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
