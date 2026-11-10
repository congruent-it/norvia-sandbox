using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Norvia.Tms.TestSupport;

/// <summary>
/// The TMS with the Agents switched on (<c>Agents:Enabled</c>), as a developer runs it: the MCP stand-in that plays API Management's MCP server, calling the TMS API's agent view
/// behind it. The tests call the stand-in with tokens of their own, signed with the key the AppHost holds, and read what the TMS API and the stand-in logged to see the hops on the Message trail.
/// The stand-in allows few calls a minute (<see cref="RateLimitCalls"/>), so the rate limit takes a few calls to reach. It counts them for each Agent identity, so each test that
/// makes calls uses an identity of its own and none depends on another.
/// </summary>
public abstract class AgentMcpFixture : TmsFixture
{
    public const int RateLimitCalls = 10;
    private const string StandInName = "agent-mcp";

    private readonly ConcurrentQueue<TmsLog> _standInLogs = new();

    /// <summary>Every log the MCP stand-in has written so far.</summary>
    public IEnumerable<TmsLog> StandInLogs => _standInLogs;

    protected override string[] AppHostArguments => ["Agents:Enabled=true"];

    /// <summary>The MCP endpoint a runner connects to, at the path of the Agent's server.</summary>
    protected Uri McpEndpoint(string path) => new(App.GetEndpoint(StandInName, "http"), path);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await WaitForHealthyAsync(StandInName, CancellationToken.None);
        CollectStandInLogs();
    }

    /// <summary>A new Agent identity, so the rate limit of one test never counts another's calls.</summary>
    public static string NewAgentId() => $"norvia-test-agent-{Guid.NewGuid():N}";

    /// <summary>A client of the stand-in that sends the given token, or none.</summary>
    public HttpClient CreateMcpClient(string? token)
    {
        var client = App.CreateHttpClient(StandInName);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

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
        builder.RemoveWhatTheTmsAgentTestsDontUse();
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == StandInName))
            .WithEnvironment("AgentMcp__RateLimitCalls", RateLimitCalls.ToString(CultureInfo.InvariantCulture))
            // The stand-in writes its logs as JSON with their scopes, like the TMS, so a test can follow a Message trail through it.
            .WithEnvironment("LOGGING__CONSOLE__FORMATTERNAME", "json")
            .WithEnvironment("Logging__Console__FormatterOptions__IncludeScopes", "true");
    }

    private void CollectStandInLogs()
    {
        var logs = App.Services.GetRequiredService<ResourceLoggerService>();
        var standIn = App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(resource => resource.Name == StandInName);
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
