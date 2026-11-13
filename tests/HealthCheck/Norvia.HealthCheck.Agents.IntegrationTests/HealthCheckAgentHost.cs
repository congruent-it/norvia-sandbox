extern alias AgentMcp;
extern alias HealthCheckWeb;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Estate;
using AppProgram = HealthCheckWeb::Program;
using McpProgram = AgentMcp::Program;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The Estate health check and the MCP stand-in, each the real application in memory, configured as the AppHost configures them with
/// <c>Agents:Enabled</c> and calling each other over their in-memory servers: the app runs the Agent in process against the stand-in's
/// <c>/mcp/health-check</c>, and the stand-in reads the app's agent API. The two share one key, as the AppHost's local key is. What they trace goes
/// to <see cref="Telemetry"/>. Each test starts a pair of its own.
/// </summary>
internal sealed class HealthCheckAgentHost : IAsyncDisposable
{
    public const string AgentId = "norvia-local-agent-estate-health-check";
    public const string Role = "HealthCheck.Agent.Read";
    public const string Issuer = "https://norvia.local/tokens";
    public const string AgentAudience = "api://norvia-health-check-agent";
    public const string ApiAudience = "api://norvia-health-check-agent-api";

    // The FinOps agent (#511): an identity, an audience and a call role of its own, on a server of its own.
    public const string CostAgentId = "norvia-local-agent-finops";
    public const string CostRole = "HealthCheck.Cost.Read";
    public const string CostAgentAudience = "api://norvia-health-check-cost-agent";

    /// <summary>The day the local cost snapshots are modelled to end the day before: the app's clock is read as this day, shifted, so the cost view has the snapshot's two periods whatever day the test runs.</summary>
    public static readonly DateOnly SnapshotToday = new(2026, 11, 15);

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(64);
    private readonly AppFactory _app;
    private readonly StandInFactory _standIn;
    private readonly Check[] _extraChecks;
    private readonly string _costSnapshot;
    private readonly Func<EstateConfiguration, EstateConfiguration>? _adjustEstate;

    /// <param name="snapshot">The estate the app reads: <c>as-found</c> or <c>fixed</c>.</param>
    /// <param name="rateLimitCalls">Calls per Agent identity the stand-in allows in a minute.</param>
    /// <param name="appDistrustsStandIn">Whether the app's agent API takes another key than the one the stand-in signs with, so every tool call is refused.</param>
    /// <param name="adjustEstate">Changes the facts the snapshot gives, such as putting an alert on a flow that failed, which no snapshot holds.</param>
    /// <param name="costSnapshot">The modelled cost the app reads: <c>fixed</c> (flat) or <c>chatty-telemetry</c>, which the <c>incident-2</c> snapshot's Finding is about.</param>
    /// <param name="extraChecks">Checks the checklist has besides its own, which a test gives evidence to mask.</param>
    public HealthCheckAgentHost(string snapshot = "as-found", int rateLimitCalls = 60, bool appDistrustsStandIn = false, Func<EstateConfiguration, EstateConfiguration>? adjustEstate = null, string costSnapshot = "fixed", params Check[] extraChecks)
    {
        _costSnapshot = costSnapshot;
        _adjustEstate = adjustEstate;
        _extraChecks = extraChecks;
        var apiKey = appDistrustsStandIn ? RandomNumberGenerator.GetBytes(64) : _key;
        _app = new AppFactory(this, snapshot, apiKey);
        _standIn = new StandInFactory(this, rateLimitCalls);
    }

    public TrackedStore Telemetry { get; } = new();

    /// <summary>The app, which starts its server on first use.</summary>
    public IServiceProvider AppServices => _app.Services;

    /// <summary>A client of the stand-in, with an Agent's token unless <paramref name="token"/> says otherwise (an empty one sends none).</summary>
    public HttpClient StandInClient(string? token = null) => WithToken(_standIn.CreateClient(), token);

    /// <summary>A client of the FinOps agent's server on the stand-in, <c>/mcp/health-check-cost</c>, with the FinOps agent's token unless <paramref name="token"/> says otherwise (an empty one sends none).</summary>
    public HttpClient CostStandInClient(string? token = null) => WithToken(_standIn.CreateClient(), token ?? CostToken());

    /// <summary>A client of the app, with an Agent's token for the app's agent API unless <paramref name="token"/> says otherwise (an empty one sends none).</summary>
    public HttpClient AppClient(string? token = null) => WithToken(_app.CreateClient(), token, ApiAudience);

    /// <summary>An Agent's token as Entra issues it to an Agent identity; change any part to show what is refused.</summary>
    public string AgentToken(string[]? roles = null, byte[]? signingKey = null, string audience = AgentAudience, string agentId = AgentId)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = agentId, ["roles"] = roles ?? [Role] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? _key), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>The FinOps agent's token for its own server, or, with another audience, for the app's agent API.</summary>
    public string CostToken(string[]? roles = null, string audience = CostAgentAudience) => AgentToken(roles ?? [CostRole], audience: audience, agentId: CostAgentId);

    /// <summary>A token signed with a key the stand-in and the app don't trust.</summary>
    public string ForgedAgentToken(string audience = AgentAudience) => AgentToken(signingKey: RandomNumberGenerator.GetBytes(64), audience: audience);

    public async ValueTask DisposeAsync()
    {
        await _standIn.DisposeAsync();
        await _app.DisposeAsync();
    }

    private HttpClient WithToken(HttpClient client, string? token, string audience = AgentAudience)
    {
        if (token != string.Empty)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? AgentToken(audience: audience));
        }

        return client;
    }

    private static void Apply(IWebHostBuilder builder, IEnumerable<KeyValuePair<string, string>> settings)
    {
        builder.UseEnvironment("Development");
        foreach (var (name, value) in settings)
        {
            builder.UseSetting(name, value);
        }
    }

    private sealed class AppFactory(HealthCheckAgentHost host, string snapshot, byte[] apiKey) : WebApplicationFactory<AppProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Apply(builder, new Dictionary<string, string>
            {
                ["LocalOperator:Name"] = "Iris Operator",
                ["LocalOperator:Roles:0"] = "HealthCheck.Run",
                ["LocalOperator:Roles:1"] = "HealthCheck.Read",
                ["Estate:Snapshot"] = snapshot,
                ["Estate:CostSnapshot"] = host._costSnapshot,
                ["Agents:Enabled"] = "true",
                ["Agents:McpAddress"] = "http://agent-mcp/mcp/health-check",
                ["Agents:Tokens:Issuer"] = Issuer,
                ["Agents:Tokens:Audience"] = AgentAudience,
                ["Agents:Tokens:ClientId"] = AgentId,
                ["Agents:Tokens:Role"] = Role,
                ["Agents:Tokens:SigningKey"] = Convert.ToBase64String(host._key),
                ["Agents:Cost:McpAddress"] = "http://agent-mcp/mcp/health-check-cost",
                ["Agents:Cost:Tokens:Issuer"] = Issuer,
                ["Agents:Cost:Tokens:Audience"] = CostAgentAudience,
                ["Agents:Cost:Tokens:ClientId"] = CostAgentId,
                ["Agents:Cost:Tokens:Role"] = CostRole,
                ["Agents:Cost:Tokens:SigningKey"] = Convert.ToBase64String(host._key),
                ["Agents:Api:Issuer"] = Issuer,
                ["Agents:Api:Audience"] = ApiAudience,
                ["Agents:Api:SigningKey"] = Convert.ToBase64String(apiKey),
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("health-check"));
                if (host._adjustEstate is not null)
                {
                    var snapshotAdapter = services.Single(service => service.ServiceType == typeof(IEstateConfiguration)).ImplementationType!;
                    services.AddSingleton<IEstateConfiguration>(provider =>
                        new AdjustedEstate((IEstateConfiguration)ActivatorUtilities.CreateInstance(provider, snapshotAdapter), host._adjustEstate));
                }

                // The cost view reads the two periods ending yesterday by the clock; the clock is shifted, not the app's, which signs its tokens by it.
                var costReader = services.Single(service => service.ServiceType == typeof(ICostReader)).ImplementationType!;
                services.AddSingleton<ICostReader>(provider =>
                    new ClockShiftedCostReader((ICostReader)ActivatorUtilities.CreateInstance(provider, costReader), provider.GetRequiredService<TimeProvider>()));

                if (host._extraChecks.Length > 0)
                {
                    services.AddSingleton(new Checklist([.. Checklist.Default.Checks, .. host._extraChecks]));
                }

                // The typed client of the app's Agent, named after its port.
                services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => host._standIn.Server.CreateHandler());
            });
        }
    }

    private sealed class StandInFactory(HealthCheckAgentHost host, int rateLimitCalls) : WebApplicationFactory<McpProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var key = Convert.ToBase64String(host._key);
            Apply(builder, new Dictionary<string, string>
            {
                // The stand-in's first server needs its settings to start; this test uses the second.
                ["AgentMcp:Tokens:Issuer"] = Issuer,
                ["AgentMcp:Tokens:Audience"] = "api://norvia-integration-console-agent",
                ["AgentMcp:Tokens:SigningKey"] = key,
                ["AgentMcp:ConsoleAgentApi"] = "http://integration-console/agent/v1",
                ["AgentMcp:ConsoleTokens:Issuer"] = Issuer,
                ["AgentMcp:ConsoleTokens:Audience"] = "api://norvia-integration-console-agent-api",
                ["AgentMcp:ConsoleTokens:SigningKey"] = key,
                ["AgentMcp:HealthCheck:Tokens:Issuer"] = Issuer,
                ["AgentMcp:HealthCheck:Tokens:Audience"] = AgentAudience,
                ["AgentMcp:HealthCheck:Tokens:SigningKey"] = key,
                ["AgentMcp:HealthCheck:Api"] = "http://health-check/agent/v1",
                ["AgentMcp:HealthCheck:ApiTokens:Issuer"] = Issuer,
                ["AgentMcp:HealthCheck:ApiTokens:Audience"] = ApiAudience,
                ["AgentMcp:HealthCheck:ApiTokens:SigningKey"] = key,
                ["AgentMcp:HealthCheckCost:Tokens:Issuer"] = Issuer,
                ["AgentMcp:HealthCheckCost:Tokens:Audience"] = CostAgentAudience,
                ["AgentMcp:HealthCheckCost:Tokens:SigningKey"] = key,
                ["AgentMcp:HealthCheckCost:Api"] = "http://health-check/agent/v1",
                ["AgentMcp:HealthCheckCost:ApiTokens:Issuer"] = Issuer,
                ["AgentMcp:HealthCheckCost:ApiTokens:Audience"] = ApiAudience,
                ["AgentMcp:HealthCheckCost:ApiTokens:SigningKey"] = key,
                ["AgentMcp:RateLimitCalls"] = rateLimitCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("agent-mcp"));
                services.AddHttpClient("console-agent-api").ConfigurePrimaryHttpMessageHandler(() => host._app.Server.CreateHandler());
            });
        }
    }

    /// <summary>Answers a read of the days before the real yesterday with the snapshot's days before <see cref="SnapshotToday"/>, dated as the real ones.</summary>
    private sealed class ClockShiftedCostReader(ICostReader inner, TimeProvider time) : ICostReader
    {
        public async Task<IReadOnlyList<CostDay>> ReadAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
        {
            var shift = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).DayNumber - SnapshotToday.DayNumber;
            var days = await inner.ReadAsync(firstDay.AddDays(-shift), lastDay.AddDays(-shift), cancellationToken);
            return [.. days.Select(day => day with { Date = day.Date.AddDays(shift) })];
        }
    }

    private sealed class AdjustedEstate(IEstateConfiguration inner, Func<EstateConfiguration, EstateConfiguration> adjust) : IEstateConfiguration
    {
        public async Task<EstateConfiguration> ReadAsync(CancellationToken cancellationToken, IProgress<string>? progress = null) => adjust(await inner.ReadAsync(cancellationToken, progress));
    }
}
