extern alias AgentMcp;
extern alias ConsoleWeb;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using ConsoleProgram = ConsoleWeb::Program;
using McpProgram = AgentMcp::Program;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// The Integration console and the MCP stand-in, each the real application in memory, configured as the AppHost configures them with
/// <c>Agents:Enabled</c> and calling each other over their in-memory servers: the console runs the Agent in process against the stand-in, and the
/// stand-in reads the console's agent API. The two share one key, as the AppHost's local key is. What they trace goes to <see cref="Telemetry"/>,
/// which the console reads its Message trails from; the Logic Apps' run history is <see cref="Runs"/>. Each test starts a pair of its own.
/// </summary>
internal sealed class AgentHost : IAsyncDisposable
{
    public const string AgentId = "norvia-local-agent-integration-operations";
    public const string Role = "IntegrationConsole.Agent.Read";
    public const string Issuer = "https://norvia.local/tokens";
    public const string AgentAudience = "api://norvia-integration-console-agent";
    public const string ConsoleAudience = "api://norvia-integration-console-agent-api";

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(64);
    private readonly ConsoleFactory _console;
    private readonly StandInFactory _standIn;
    private readonly IReadOnlyDictionary<string, string> _consoleSettings;

    /// <param name="rateLimitCalls">Calls per Agent identity the stand-in allows in a minute.</param>
    /// <param name="consoleDistrustsStandIn">Whether the console's agent API takes another key than the one the stand-in signs with, so every tool call is refused.</param>
    /// <param name="consoleSettings">More configuration for the console, such as the Service Bus emulators it lists locally (<c>LocalServiceBus</c>).</param>
    public AgentHost(int rateLimitCalls = 60, bool consoleDistrustsStandIn = false, IReadOnlyDictionary<string, string>? consoleSettings = null)
    {
        _consoleSettings = consoleSettings ?? new Dictionary<string, string>();
        var apiKey = consoleDistrustsStandIn ? RandomNumberGenerator.GetBytes(64) : _key;
        _console = new ConsoleFactory(this, apiKey);
        _standIn = new StandInFactory(this, rateLimitCalls);
    }

    public TrackedStore Telemetry { get; } = new();

    public StubRuns Runs { get; } = new();

    /// <summary>The failed runs the console lists, which a test gives it.</summary>
    public StubFailedRuns FailedRuns { get; } = new();

    /// <summary>The console, which starts its server on first use.</summary>
    public IServiceProvider ConsoleServices => _console.Services;

    /// <summary>A client of the stand-in's <c>/mcp</c>, with an Agent's token unless <paramref name="token"/> says otherwise (an empty one sends none).</summary>
    public HttpClient StandInClient(string? token = null)
    {
        var client = _standIn.CreateClient();
        if (token != string.Empty)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? AgentToken());
        }

        return client;
    }

    /// <summary>An Agent's token as Entra issues it to an Agent identity; change any part to show what is refused.</summary>
    public string AgentToken(string[]? roles = null, byte[]? signingKey = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = AgentAudience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = AgentId, ["roles"] = roles ?? [Role] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? _key), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>A token signed with a key the stand-in doesn't trust.</summary>
    public string ForgedAgentToken() => AgentToken(signingKey: RandomNumberGenerator.GetBytes(64));

    public async ValueTask DisposeAsync()
    {
        await _standIn.DisposeAsync();
        await _console.DisposeAsync();
    }

    private static void Apply(IWebHostBuilder builder, IEnumerable<KeyValuePair<string, string>> settings)
    {
        builder.UseEnvironment("Development");
        foreach (var (name, value) in settings)
        {
            builder.UseSetting(name, value);
        }
    }

    private sealed class ConsoleFactory(AgentHost host, byte[] apiKey) : WebApplicationFactory<ConsoleProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var agentKey = Convert.ToBase64String(host._key);
            Apply(builder, new Dictionary<string, string>(host._consoleSettings)
            {
                ["LocalOperator:Name"] = "Iris Operator",
                ["LocalOperator:Roles:0"] = "IntegrationConsole.Read",
                ["Estate:IntegrationRetentionDays"] = "90",
                ["Agents:Enabled"] = "true",
                ["Agents:McpAddress"] = "http://agent-mcp/mcp",
                ["Agents:Tokens:Issuer"] = Issuer,
                ["Agents:Tokens:Audience"] = AgentAudience,
                ["Agents:Tokens:ClientId"] = AgentId,
                ["Agents:Tokens:Role"] = Role,
                ["Agents:Tokens:SigningKey"] = agentKey,
                ["Agents:Api:Issuer"] = Issuer,
                ["Agents:Api:Audience"] = ConsoleAudience,
                ["Agents:Api:SigningKey"] = Convert.ToBase64String(apiKey),
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("integration-console"));
                services.AddSingleton<ITrailSource>(host.Telemetry);
                services.AddSingleton<IWorkflowRuns>(host.Runs);
                services.AddSingleton<IFailedRuns>(host.FailedRuns);
                // The typed client of the console's Agent, named after its port.
                services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => host._standIn.Server.CreateHandler());
            });
        }
    }

    private sealed class StandInFactory(AgentHost host, int rateLimitCalls) : WebApplicationFactory<McpProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var key = Convert.ToBase64String(host._key);
            Apply(builder, new Dictionary<string, string>
            {
                ["AgentMcp:Tokens:Issuer"] = Issuer,
                ["AgentMcp:Tokens:Audience"] = AgentAudience,
                ["AgentMcp:Tokens:SigningKey"] = key,
                ["AgentMcp:ConsoleAgentApi"] = "http://integration-console/agent/v1",
                ["AgentMcp:ConsoleTokens:Issuer"] = Issuer,
                ["AgentMcp:ConsoleTokens:Audience"] = ConsoleAudience,
                ["AgentMcp:ConsoleTokens:SigningKey"] = key,
                ["AgentMcp:RateLimitCalls"] = rateLimitCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("agent-mcp"));
                services.AddHttpClient("console-agent-api").ConfigurePrimaryHttpMessageHandler(() => host._console.Server.CreateHandler());
            });
        }
    }
}
