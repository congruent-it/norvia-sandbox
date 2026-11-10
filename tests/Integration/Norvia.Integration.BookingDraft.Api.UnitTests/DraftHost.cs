using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft.Api.UnitTests;

/// <summary>The host wired as Program.cs wires it, with Entra's keys swapped for a test key and the Foundry client for a scripted Agent.</summary>
internal sealed class DraftHost : IAsyncDisposable
{
    public const string Issuer = "https://login.test/tenant/v2.0";
    public const string Audience = "api://norvia-booking-draft-api";
    public const string Gateway = "00000000-0000-0000-0000-00000000a9a1";

    private readonly WebApplication _app;
    private readonly byte[] _key = new byte[64];

    private DraftHost(WebApplication app, byte[] key, FakeLogCollector logs, ScriptedAgent agent)
    {
        _app = app;
        _key = key;
        Logs = logs;
        Agent = agent;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public FakeLogCollector Logs { get; }

    public ScriptedAgent Agent { get; }

    public static async Task<DraftHost> StartAsync(bool enabled = true, Func<string>? reply = null, int maxCharacters = 8_000)
    {
        var key = new byte[64];
        Random.Shared.NextBytes(key);
        var logs = new FakeLogCollector();
        var agent = new ScriptedAgent(reply ?? (() => """{"booking": {}, "quotes": {}, "missing": [{"field": "booking", "reason": "No booking."}]}"""));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FakeLoggerProvider(logs));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agents:Enabled"] = enabled.ToString(),
            ["Agents:MaxInputCharacters"] = maxCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Agents:Api:Issuer"] = Issuer,
            ["Agents:Api:Audience"] = Audience,
            ["Agents:Api:GatewayClientId"] = Gateway,
        });
        builder.Services.AddOptions<BookingDraftHostOptions>().BindConfiguration(BookingDraftHostOptions.SectionName);
        builder.AddGatewayAuthentication();
        builder.Services.PostConfigure<JwtBearerOptions>(GatewayAuthentication.Scheme, options =>
        {
            options.Authority = null;
            options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(key);
        });
        builder.Services.AddSingleton<IAgentClient>(agent);
        builder.Services.AddSingleton(services => new BookingDraftExtractor(services.GetRequiredService<IAgentClient>()));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBookingDraft();
        await app.StartAsync();
        return new DraftHost(app, key, logs, agent);
    }

    public string Token(string azp = Gateway, string[]? roles = null, string audience = Audience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["azp"] = azp, ["roles"] = roles ?? [GatewayAuthentication.Role] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256),
        });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

internal sealed class ScriptedAgent(Func<string> reply) : IAgentClient
{
    public List<AgentQuestion> Asked { get; } = [];

    public Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken)
    {
        Asked.Add(question);
        return Task.FromResult(reply());
    }
}
