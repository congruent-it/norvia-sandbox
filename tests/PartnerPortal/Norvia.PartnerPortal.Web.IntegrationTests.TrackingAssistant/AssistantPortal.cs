extern alias PortalWeb;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Norvia.Agents.Scripts;
using PortalWeb::Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.IntegrationTests.TrackingAssistant;

/// <summary>
/// The Partner portal in the tests' process, with what a test looks at on its way: every call the portal made to another service, every call to the model, and everything it logged.
/// The model is the scripted fake unless a test puts another in its place for a run.
/// </summary>
public sealed class AssistantPortal : IAsyncDisposable
{
    private readonly PortalFactory _factory;

    internal AssistantPortal(IReadOnlyDictionary<string, string> settings)
    {
        Settings = settings;
        Model = new RecordingChatClient(ShipperTrackingAssistantScript.Model());
        _factory = new PortalFactory(settings, Model, Calls, Logs);
        Portal = _factory.Services.GetRequiredService<IPartnerPortal>();
    }

    public IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>What the portal's pages ask of it.</summary>
    public IPartnerPortal Portal { get; }

    /// <summary>The calls to the model.</summary>
    public RecordingChatClient Model { get; }

    /// <summary>Every HTTP call the portal made to another service.</summary>
    public CallRecorder Calls { get; } = new();

    /// <summary>Everything the portal logged: the messages, the properties they were written with and the keys of the scopes they were written in.</summary>
    public LogCapture Logs { get; } = new();

    /// <summary>The subscription key and the client ID of the Persona, as the AppHost gave them to the portal.</summary>
    public (string SubscriptionKey, string ClientId) CredentialsOf(string persona)
    {
        var clientId = Settings["PartnerApi:PersonaClientIds"].Split(';').Select(pair => pair.Split('=', 2)).Single(pair => pair[0] == persona)[1];
        return (Settings[$"PartnerApi:SubscriptionKeys:{persona}"], clientId);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private sealed class PortalFactory(IReadOnlyDictionary<string, string> settings, IChatClient model, CallRecorder calls, LogCapture logs) : WebApplicationFactory<PortalWeb::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var (name, value) in settings)
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                // The seams a test reaches: the model the in-process Agent runs on, and a handler on every outbound call.
                services.AddSingleton(model);
                services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler(_ => new RecordingHandler(calls)));
            });
        }
    }
}

/// <summary>One call the portal made to another service.</summary>
public sealed record RecordedCall(HttpMethod Method, Uri Uri, string? SubscriptionKey, string? Token, int StatusCode, string? CorrelationId)
{
    /// <summary>The <c>azp</c> claim of the Persona's token: the client the token was issued to.</summary>
    public string? ClientId => Token is null ? null : new JsonWebTokenHandler().ReadJsonWebToken(Token).GetClaim("azp")?.Value;
}

public sealed class CallRecorder
{
    private readonly ConcurrentQueue<RecordedCall> _calls = new();

    public IReadOnlyList<RecordedCall> All => [.. _calls];

    /// <summary>The calls to the Partner API's Transport Order operations, which are the tracking fetches.</summary>
    public IReadOnlyList<RecordedCall> Tracking => [.. _calls.Where(call => call.Uri.AbsolutePath.Contains("/transport-orders", StringComparison.Ordinal))];

    internal void Add(RecordedCall call) => _calls.Enqueue(call);
}

internal sealed class RecordingHandler(CallRecorder calls) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var subscriptionKey = request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys) ? keys.FirstOrDefault() : null;
        var statusCode = 0;
        string? correlationId = null;
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            statusCode = (int)response.StatusCode;
            correlationId = response.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids.FirstOrDefault() : null;
            return response;
        }
        finally
        {
            calls.Add(new RecordedCall(request.Method, request.RequestUri!, subscriptionKey, request.Headers.Authorization?.Parameter, statusCode, correlationId));
        }
    }
}

/// <summary>The model the Agent runs on: the scripted fake, recording each call's input and tool list, or another a test puts in its place.</summary>
public sealed class RecordingChatClient(IChatClient scripted) : IChatClient
{
    private readonly ConcurrentQueue<ModelCall> _calls = new();
    private volatile IChatClient _inner = scripted;

    public IReadOnlyList<ModelCall> Calls => [.. _calls];

    /// <summary>Puts a model in the place of the scripted fake for the runs that follow.</summary>
    public void Use(IChatClient other) => _inner = other;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var all = messages.ToList();
        _calls.Enqueue(new ModelCall(string.Join('\n', all.Select(message => message.Text)), options?.Tools?.Count ?? 0));
        return await _inner.GetResponseAsync(all, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

/// <summary>One call to the model: everything it was sent, and how many tools it was offered.</summary>
public sealed record ModelCall(string Input, int ToolCount);

/// <summary>Keeps every string the portal logged, so a test can show that a text is in none of them.</summary>
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _strings = new();
    private IExternalScopeProvider? _scopes;

    public IReadOnlyList<string> Strings => [.. _strings];

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            capture._strings.Enqueue(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var (key, value) in properties)
                {
                    capture._strings.Enqueue($"{key}={value}");
                }
            }

            capture._scopes?.ForEachScope(
                (scope, keep) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs)
                        {
                            keep._strings.Enqueue($"{key}={value}");
                        }
                    }
                    else
                    {
                        keep._strings.Enqueue(scope?.ToString() ?? string.Empty);
                    }
                },
                capture);
        }
    }
}
