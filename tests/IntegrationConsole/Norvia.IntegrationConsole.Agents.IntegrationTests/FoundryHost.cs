extern alias ConsoleWeb;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using ConsoleProgram = ConsoleWeb::Program;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// The Integration console as it runs in Azure (ADR 0040): <c>Agents:Foundry:ProjectEndpoint</c> set, so its <c>IAgentClient</c> is the Foundry client
/// built by the console's own registration, over a real HTTP pipeline whose far end is <see cref="Responses"/>, the project's Responses endpoint.
/// What the console traces goes to <see cref="Telemetry"/>, which it reads its Message trails from. Each test starts a console of its own.
/// </summary>
internal sealed class FoundryHost : IAsyncDisposable
{
    public const string Project = "https://ais-norvia.services.ai.azure.com/api/projects/norvia";

    private readonly ConsoleFactory _console;

    public FoundryHost(int timeoutSeconds = 5)
    {
        _console = new ConsoleFactory(this, timeoutSeconds);
    }

    public TrackedStore Telemetry { get; } = new();

    public FakeResponses Responses { get; } = new();

    public IServiceProvider ConsoleServices => _console.Services;

    public ValueTask DisposeAsync()
    {
        Responses.Dispose();
        return _console.DisposeAsync();
    }

    private sealed class ConsoleFactory(FoundryHost host, int timeoutSeconds) : WebApplicationFactory<ConsoleProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            foreach (var (name, value) in new Dictionary<string, string>
            {
                ["LocalOperator:Name"] = "Iris Operator",
                ["LocalOperator:Roles:0"] = "IntegrationConsole.Read",
                ["Estate:IntegrationRetentionDays"] = "90",
                ["Agents:Enabled"] = "true",
                ["Agents:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Agents:Foundry:ProjectEndpoint"] = Project,
                ["Agents:Api:Issuer"] = "https://norvia.local/tokens",
                ["Agents:Api:Audience"] = "api://norvia-integration-console-agent-api",
                ["Agents:Api:SigningKey"] = key,
            })
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("integration-console"));
                services.AddSingleton<ITrailSource>(host.Telemetry);
                services.AddSingleton<IWorkflowRuns>(new StubRuns());
                services.AddSingleton<TokenCredential>(new FixedCredential());
                // The typed client of the console's Agent, named after its port; its far end is the project.
                services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => host.Responses);
            });
        }
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
        ["server_label"] = "integration-operations",
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
