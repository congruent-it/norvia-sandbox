using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// The estate with the Agents switched on (<c>Agents:Enabled</c>), as a developer runs it, cut down to the ERP (API, UI and database), the MCP stand-in that plays API Management's MCP
/// server, serving the Carrier invoice matching agent's tool at <c>/mcp/finance-matching</c>, and what the stand-in waits for. The tests call the stand-in and the ERP API with tokens of their own,
/// signed with the key the AppHost holds, and read what the ERP API and the stand-in logged to see the hops on the Message trail.
/// The stand-in allows few calls a minute (<see cref="RateLimitCalls"/>), so the rate limit takes a few calls to reach. It counts them for each Agent identity, so each test that makes
/// calls uses an identity of its own and none depends on another. The tests share the database and each records the Carrier costs and Carrier invoices it needs under numbers of its own.
/// </summary>
public sealed class FinanceAgentFixture : IAsyncLifetime
{
    public const string McpPath = "/mcp/finance-matching";
    public const int RateLimitCalls = 10;

    /// <summary>The app role only the Carrier cost Logic App's managed identity holds.</summary>
    public const string RecordCarrierCostRole = "CarrierCosts.Record";

    /// <summary>The app role only the Carrier invoice Logic App's managed identity holds, for the Carrier invoices it receives.</summary>
    public const string ReceiveCarrierInvoiceRole = "CarrierInvoices.Receive";

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(8);

    // What the tests don't use: the other applications' resources and the Logic App workers. The ERP, the TMS (which the stand-in waits for), the Estate health check and the
    // Integration console (the same), SQL Server and the Service Bus emulator stay.
    private static readonly string[] Unneeded =
    [
        "dbgate", "partner-portal", "partner-portal-migrations", "partner-portal-web", "partner-api",
        "driver-app-migrations", "driver-app-api", "driver-app-web",
        "simulator", "simulator-migrations", "simulator-web",
        "booking-worker", "shipper-notification-worker", "carrier-cancellation-worker", "trip-dispatch-worker", "driver-reports-worker", "tracking-worker", "proof-of-delivery-worker",
        "billing-worker", "carrier-cost-worker", "shipper-invoice-worker", "carrier-invoice-outcome-worker", "carrier-invoice-worker",
    ];

    private readonly string _tokenSigningKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentQueue<TrailLog> _standInLogs = new();
    private readonly ConcurrentQueue<TrailLog> _erpLogs = new();
    private DistributedApplication? _app;

    /// <summary>Every log the MCP stand-in has written so far.</summary>
    public IEnumerable<TrailLog> StandInLogs => _standInLogs;

    /// <summary>Every log the ERP API has written so far.</summary>
    public IEnumerable<TrailLog> ErpLogs => _erpLogs;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}", "Agents:Enabled=true"],
            startup.Token);
        foreach (var resource in builder.Resources
            .Where(resource => Unneeded.Any(name => resource.Name == name || resource.Name == $"{name}-rebuilder"))
            .ToList())
        {
            builder.Resources.Remove(resource);
        }

        foreach (var name in new[] { "agent-mcp", "erp-api" })
        {
            // The applications write their logs as JSON with their scopes, so a test can follow a Message trail through them.
            builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == name))
                .WithEnvironment("LOGGING__CONSOLE__FORMATTERNAME", "json")
                .WithEnvironment("Logging__Console__FormatterOptions__IncludeScopes", "true");
        }

        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "agent-mcp"))
            .WithEnvironment("AgentMcp__RateLimitCalls", RateLimitCalls.ToString(System.Globalization.CultureInfo.InvariantCulture));

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("erp-api", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("agent-mcp", startup.Token);
        Collect("agent-mcp", _standInLogs);
        Collect("erp-api", _erpLogs);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A new Agent identity, so the rate limit of one test never counts another's calls.</summary>
    public static string NewAgentId() => $"norvia-test-agent-{Guid.NewGuid():N}";

    /// <summary>A token as Entra issues it to the Carrier invoice matching agent's identity for the MCP server; change any part to show what is refused.</summary>
    public string AgentToken(string? agentId = null, string[]? roles = null, string? signingKey = null, string? audience = null, TimeSpan? lifetime = null) =>
        IssueToken(roles ?? [LocalTokens.FinanceAgentReadRole], audience ?? LocalTokens.FinanceAgentAudience, agentId ?? NewAgentId(), signingKey, lifetime);

    /// <summary>A token for the ERP API's agent view itself, as the stand-in signs it for the Agent's identity: the ERP API's audience, with the roles given.</summary>
    public string AgentViewToken(string[] roles, string? agentId = null, string? signingKey = null) => IssueToken(roles, LocalTokens.ErpAudience, agentId ?? NewAgentId(), signingKey);

    /// <summary>A client of the stand-in that sends the given token, or none.</summary>
    public HttpClient CreateMcpClient(string? token) => WithToken(_app!.CreateHttpClient("agent-mcp"), token);

    /// <summary>A client of the ERP API that sends the given token, or none.</summary>
    public HttpClient CreateErpClient(string? token) => WithToken(_app!.CreateHttpClient("erp-api"), token);

    /// <summary>A client of the ERP API holding the roles, as the Logic App identities holding them call.</summary>
    public HttpClient ErpAs(params string[] roles) => CreateErpClient(IssueToken(roles, LocalTokens.ErpAudience, "norvia-test-logic-app"));

    /// <summary>The MCP endpoint a runner connects to.</summary>
    public Uri McpAddress => new(_app!.GetEndpoint("agent-mcp", "http"), McpPath);

    /// <summary>Waits for the stand-in to write a log that matches, as logs arrive a moment after the call.</summary>
    public Task<TrailLog> StandInLogAsync(Func<TrailLog, bool> match, CancellationToken cancellationToken) => WaitForAsync(_standInLogs, match, cancellationToken);

    /// <summary>Waits for the ERP API to write a log that matches, as logs arrive a moment after the call.</summary>
    public Task<TrailLog> ErpLogAsync(Func<TrailLog, bool> match, CancellationToken cancellationToken) => WaitForAsync(_erpLogs, match, cancellationToken);

    private static async Task<TrailLog> WaitForAsync(ConcurrentQueue<TrailLog> logs, Func<TrailLog, bool> match, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!logs.Any(match))
        {
            try
            {
                await Task.Delay(PollInterval, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var lastSeen = string.Join(Environment.NewLine, logs.TakeLast(5).Select(log => string.Join(", ", log.Scope.Select(pair => $"{pair.Key}={pair.Value}"))));
                throw new TimeoutException($"No log matched in 30 seconds. {logs.Count} logs with scopes were written; the last:{Environment.NewLine}{lastSeen}");
            }
        }

        return logs.First(match);
    }

    private static HttpClient WithToken(HttpClient client, string? token)
    {
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private string IssueToken(string[] roles, string audience, string clientId, string? signingKey = null, TimeSpan? lifetime = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = audience,
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow + (lifetime ?? TimeSpan.FromHours(1)),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? _tokenSigningKey)), SecurityAlgorithms.HmacSha256),
        });

    private void Collect(string resourceName, ConcurrentQueue<TrailLog> into)
    {
        var logs = _app!.Services.GetRequiredService<ResourceLoggerService>();
        var resource = _app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(candidate => candidate.Name == resourceName);
        _ = Task.Run(async () =>
        {
            await foreach (var batch in logs.WatchAsync(resource))
            {
                foreach (var line in batch)
                {
                    if (TrailLog.TryParse(line.Content) is { } log)
                    {
                        into.Enqueue(log);
                    }
                }
            }
        });
    }
}
