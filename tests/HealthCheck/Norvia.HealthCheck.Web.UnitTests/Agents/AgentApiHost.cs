using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.HealthCheck.Web.Agents;
using Norvia.HealthCheck.Web.Operators;

namespace Norvia.HealthCheck.Web.UnitTests.Agents;

/// <summary>
/// The app's agent API in a host of its own, wired as Program.cs wires it: the operators' sign-in as the default scheme, as it is
/// locally (a developer is every operator), and the agent API's token scheme beside it.
/// </summary>
internal sealed class AgentApiHost : IAsyncDisposable
{
    public const string Issuer = "https://norvia.local/tokens";
    public const string Audience = "api://norvia-health-check-agent-api";
    public const string Role = "HealthCheck.Agent.Read";
    public const string AgentId = "norvia-local-agent-estate-health-check";

    private readonly WebApplication _app;

    private AgentApiHost(WebApplication app, byte[] signingKey, FakeLogCollector logs, IMediator mediator)
    {
        _app = app;
        SigningKey = signingKey;
        Logs = logs;
        Mediator = mediator;
        Client = app.GetTestClient();
    }

    public byte[] SigningKey { get; }

    public FakeLogCollector Logs { get; }

    public IMediator Mediator { get; }

    public HttpClient Client { get; }

    public IEnumerable<RouteEndpoint> AgentRoutes =>
        _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(AgentEndpoints.Prefix, StringComparison.Ordinal) == true);

    public static async Task<AgentApiHost> StartAsync(bool enabled = true, bool withKey = true, string? gatewayClientId = null, bool entra = false)
    {
        var signingKey = new byte[64];
        Random.Shared.NextBytes(signingKey);
        var mediator = Substitute.For<IMediator>();
        var logs = new FakeLogCollector();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FakeLoggerProvider(logs));
        var settings = new Dictionary<string, string?>
        {
            ["LocalOperator:Name"] = "Dev Eloper",
            ["LocalOperator:Roles:0"] = "HealthCheck.Run",
            ["LocalOperator:Roles:1"] = "HealthCheck.Read",
            ["Agents:Enabled"] = enabled.ToString(),
        };
        if (withKey)
        {
            settings["Agents:Api:Issuer"] = Issuer;
            settings["Agents:Api:Audience"] = Audience;
            settings["Agents:Api:SigningKey"] = Convert.ToBase64String(signingKey);
        }

        if (gatewayClientId is not null)
        {
            settings["Agents:Api:GatewayClientId"] = gatewayClientId;
        }

        if (entra)
        {
            // In Azure there is no key: Entra's metadata gives the keys. Nothing answers at this issuer here.
            settings.Remove("Agents:Api:SigningKey");
            settings["Agents:Api:Issuer"] = "https://localhost:1/tenant/v2.0";
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddOperatorSignIn();
        builder.AddAgentApi();
        builder.Services.AddSingleton(mediator);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAgentApi();
        await app.StartAsync();
        return new AgentApiHost(app, signingKey, logs, mediator);
    }

    public string Token(string? azp = AgentId, string[]? roles = null, string audience = Audience, byte[]? key = null)
    {
        var claims = new Dictionary<string, object>();
        if (azp is not null)
        {
            claims["azp"] = azp;
        }

        claims["roles"] = roles ?? [Role];
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? SigningKey), SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpRequestMessage Get(string path, string? token = null, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
