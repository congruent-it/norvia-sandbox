using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The TMS with the Agents switched on (<c>Agents:Enabled</c>), as a developer runs it: the MCP stand-in that plays API Management's MCP server, serving the
/// Customer service agent's tool at <c>/mcp/customer-service</c> and calling the TMS API's agent view behind it. The tests call the stand-in with tokens of their own,
/// signed with the key the AppHost holds, and read what the TMS API and the stand-in logged to see the hops on the Message trail.
/// The stand-in allows few calls a minute (<see cref="RateLimitCalls"/>), so the rate limit takes a few calls to reach. It counts them for each Agent identity, so each test that
/// makes calls uses an identity of its own and none depends on another.
/// </summary>
public sealed class CustomerServiceAgentFixture : TmsFixture
{
    public const string McpPath = "/mcp/customer-service";
    public const int RateLimitCalls = 10;

    private readonly System.Collections.Concurrent.ConcurrentQueue<TmsLog> _standInLogs = new();

    /// <summary>Every log the MCP stand-in has written so far.</summary>
    public IEnumerable<TmsLog> StandInLogs => _standInLogs;

    protected override string[] AppHostArguments => ["Agents:Enabled=true"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await WaitForHealthyAsync("agent-mcp", CancellationToken.None);
        CollectStandInLogs();
    }

    /// <summary>A new Agent identity, so the rate limit of one test never counts another's calls.</summary>
    public static string NewAgentId() => $"norvia-test-agent-{Guid.NewGuid():N}";

    /// <summary>A token as Entra issues it to the Customer service agent's identity for the MCP server; change any part to show what is refused.</summary>
    public string AgentToken(string? agentId = null, string[]? roles = null, string? signingKey = null) =>
        IssueToken(roles ?? [LocalTokens.CustomerServiceAgentReadRole], LocalTokens.CustomerServiceAgentAudience, agentId ?? NewAgentId(), signingKey);

    /// <summary>A client of the stand-in that sends the given token, or none.</summary>
    public HttpClient CreateMcpClient(string? token)
    {
        var client = App.CreateHttpClient("agent-mcp");
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>The MCP endpoint a runner connects to.</summary>
    public Uri McpAddress => new(App.GetEndpoint("agent-mcp", "http"), McpPath);

    /// <summary>Waits for the stand-in to write a log that matches, as logs arrive a moment after the call.</summary>
    public async Task<TmsLog> StandInLogAsync(Func<TmsLog, bool> match, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!_standInLogs.Any(match))
        {
            await Task.Delay(PollInterval, timeout.Token);
        }

        return _standInLogs.First(match);
    }

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheCustomerServiceAgentTestsDontUse();
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "agent-mcp"))
            .WithEnvironment("AgentMcp__RateLimitCalls", RateLimitCalls.ToString(System.Globalization.CultureInfo.InvariantCulture))
            // The stand-in writes its logs as JSON with their scopes, like the TMS, so a test can follow a Message trail through it.
            .WithEnvironment("LOGGING__CONSOLE__FORMATTERNAME", "json")
            .WithEnvironment("Logging__Console__FormatterOptions__IncludeScopes", "true");
    }

    private void CollectStandInLogs()
    {
        var logs = App.Services.GetRequiredService<ResourceLoggerService>();
        var standIn = App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(resource => resource.Name == "agent-mcp");
        _ = Task.Run(async () =>
        {
            await foreach (var batch in logs.WatchAsync(standIn))
            {
                foreach (var line in batch)
                {
                    if (TmsLog.TryParse(line.Content) is { } log)
                    {
                        _standInLogs.Enqueue(log);
                    }
                }
            }
        });
    }
}
