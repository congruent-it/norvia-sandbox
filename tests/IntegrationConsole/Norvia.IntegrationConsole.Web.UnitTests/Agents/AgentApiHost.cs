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
using Norvia.IntegrationConsole.Web.Agents;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

/// <summary>
/// The console's agent API in a host of its own, wired as Program.cs wires it: the operators' sign-in as the default scheme, as it is
/// locally (a developer is every operator), and the agent API's token scheme beside it.
/// </summary>
internal sealed class AgentApiHost : IAsyncDisposable
{
    public const string Issuer = "https://norvia.local/tokens";
    public const string Audience = "api://norvia-integration-console-agent-api";
    public const string Role = "IntegrationConsole.Agent.Read";

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
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(AgentTrailEndpoints.Prefix, StringComparison.Ordinal) == true);

    public static async Task<AgentApiHost> StartAsync(bool enabled = true, bool withKey = true)
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
            ["LocalOperator:Roles:0"] = "IntegrationConsole.Read",
            ["LocalOperator:Roles:1"] = "IntegrationConsole.Resubmit",
            ["Agents:Enabled"] = enabled.ToString(),
        };
        if (withKey)
        {
            settings["Agents:Api:Issuer"] = Issuer;
            settings["Agents:Api:Audience"] = Audience;
            settings["Agents:Api:SigningKey"] = Convert.ToBase64String(signingKey);
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

    public string Token(string? azp = "norvia-local-agent-integration-operations", string[]? roles = null, string audience = Audience, byte[]? key = null)
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
