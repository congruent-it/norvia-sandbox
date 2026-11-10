using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.BookingDraft.Api.IntegrationTests;

/// <summary>
/// The Booking draft host as it runs in Azure (ADR 0040): the real <c>Program</c>, so the real options, Entra token validation, authorization policy, endpoint
/// and Foundry client over a real HTTP pipeline, with only the edges replaced: Entra's signing keys by a test key, the managed identity by a fixed
/// credential and Foundry's Responses endpoint by <see cref="Responses"/>. Each test builds a host of its own.
/// </summary>
internal sealed class AzureHost : IAsyncDisposable
{
    public const string Issuer = "https://login.test/tenant/v2.0";
    public const string Audience = "api://norvia-booking-draft-api";
    public const string Gateway = "00000000-0000-0000-0000-00000000a9a1";
    public const string Project = "https://ais-norvia.services.ai.azure.com/api/projects/norvia-intake";
    public const string Route = "/draft/v1/booking-drafts";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly byte[] _key = new byte[64];
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];

    public AzureHost(bool enabled = true, int timeoutSeconds = 5, int maxCharacters = 8_000)
    {
        Random.Shared.NextBytes(_key);
        var key = _key;
        var logs = Logs;
        var responses = Responses;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Agents:Enabled", enabled.ToString());
            builder.UseSetting("Agents:TimeoutSeconds", timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Agents:MaxInputCharacters", maxCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Agents:Foundry:ProjectEndpoint", Project);
            builder.UseSetting("Agents:Api:Issuer", Issuer);
            builder.UseSetting("Agents:Api:Audience", Audience);
            builder.UseSetting("Agents:Api:GatewayClientId", Gateway);
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new FakeLoggerProvider(logs));
            });
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>("Gateway", options =>
                {
                    options.Authority = null;
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(key);
                });
                services.AddSingleton<TokenCredential>(new FixedCredential());
                // The typed client of the host's Agent, named after its port; its far end is the project.
                services.AddHttpClient("IAgentClient").ConfigurePrimaryHttpMessageHandler(() => responses);
            });
        });

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.Contains("BookingDraft", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (_spans)
                {
                    _spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
        Client = _factory.CreateClient();
    }

    public HttpClient Client { get; }

    public FakeLogCollector Logs { get; } = new();

    public FakeResponses Responses { get; } = new();

    public IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_spans)
            {
                return [.. _spans];
            }
        }
    }

    /// <summary>A token as Entra would issue it to API Management's identity for the host's API: the gateway as <c>azp</c>, the host's one role.</summary>
    public string Token(string azp = Gateway, string[]? roles = null, string audience = Audience, byte[]? signingKey = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["azp"] = azp, ["roles"] = roles ?? ["BookingDrafts.Create"] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? _key), SecurityAlgorithms.HmacSha256),
        });

    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        Client.Dispose();
        await _factory.DisposeAsync();
        Responses.Dispose();
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

    public void Answer(params JsonObject[] output) => _output = output;

    public void Answer(string status, params JsonObject[] output) => (_status, _output) = (status, output);

    public void Fail(HttpStatusCode status) => _failure = status;

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
