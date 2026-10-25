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

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(64);
    private readonly AppFactory _app;
    private readonly StandInFactory _standIn;
    private readonly Check[] _extraChecks;

    /// <param name="snapshot">The estate the app reads: <c>as-found</c> or <c>fixed</c>.</param>
    /// <param name="rateLimitCalls">Calls per Agent identity the stand-in allows in a minute.</param>
    /// <param name="appDistrustsStandIn">Whether the app's agent API takes another key than the one the stand-in signs with, so every tool call is refused.</param>
    /// <param name="extraChecks">Checks the checklist has besides its own, which a test gives evidence to mask.</param>
    public HealthCheckAgentHost(string snapshot = "as-found", int rateLimitCalls = 60, bool appDistrustsStandIn = false, params Check[] extraChecks)
    {
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

    /// <summary>A client of the app, with an Agent's token for the app's agent API unless <paramref name="token"/> says otherwise (an empty one sends none).</summary>
    public HttpClient AppClient(string? token = null) => WithToken(_app.CreateClient(), token, ApiAudience);

    /// <summary>An Agent's token as Entra issues it to an Agent identity; change any part to show what is refused.</summary>
    public string AgentToken(string[]? roles = null, byte[]? signingKey = null, string audience = AgentAudience)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = AgentId, ["roles"] = roles ?? [Role] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? _key), SecurityAlgorithms.HmacSha256),
        });
    }

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
                ["Agents:Enabled"] = "true",
                ["Agents:McpAddress"] = "http://agent-mcp/mcp/health-check",
                ["Agents:Tokens:Issuer"] = Issuer,
                ["Agents:Tokens:Audience"] = AgentAudience,
                ["Agents:Tokens:ClientId"] = AgentId,
                ["Agents:Tokens:Role"] = Role,
                ["Agents:Tokens:SigningKey"] = Convert.ToBase64String(host._key),
                ["Agents:Api:Issuer"] = Issuer,
                ["Agents:Api:Audience"] = ApiAudience,
                ["Agents:Api:SigningKey"] = Convert.ToBase64String(apiKey),
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("health-check"));
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
                ["AgentMcp:RateLimitCalls"] = rateLimitCalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(host.Telemetry.ProviderFor("agent-mcp"));
                services.AddHttpClient("console-agent-api").ConfigurePrimaryHttpMessageHandler(() => host._app.Server.CreateHandler());
            });
        }
    }
}
