using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>Lists and switches the faults the applications own, through their APIs, as the presenter.</summary>
internal sealed partial class FaultSwitchClient(
    HttpClient http,
    IFaultTokens tokens,
    IOptions<FaultsOptions> options,
    PortalActivities activities,
    ILogger<FaultSwitchClient> logger) : IFaultSwitches
{
    private const string Flow = "fault-switch";

    public async Task<IReadOnlyList<FaultView>> ListAsync(CancellationToken cancellationToken)
    {
        var faults = new List<FaultView>();
        foreach (var (owner, settings) in options.Value.Owners.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            using var request = await RequestAsync(HttpMethod.Get, settings, "v1/faults", cancellationToken);
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var listed = await response.Content.ReadFromJsonAsync<List<FaultBody>>(cancellationToken) ?? [];
            faults.AddRange(listed.Select(fault => fault.ToView(owner)));
        }

        return faults;
    }

    public async Task<FaultView> SwitchAsync(string owner, string name, TimeSpan? duration, CancellationToken cancellationToken)
    {
        var settings = options.Value.Owners.GetValueOrDefault(owner) ?? throw new ArgumentException($"No application owns faults as '{owner}'.", nameof(owner));

        // The presenter panel sends a command to the application, so it does it in a span of its own (coding guidelines §7).
        using var span = activities.Source.StartActivity("Switch fault", ActivityKind.Internal);
        var correlationId = Guid.NewGuid().ToString();
        FaultBody body;
        try
        {
            using var request = await RequestAsync(HttpMethod.Put, settings, $"v1/faults/{Uri.EscapeDataString(name)}", cancellationToken);
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Content = JsonContent.Create(new SwitchBody(duration is not null, duration is { } on ? (int)Math.Ceiling(on.TotalSeconds) : null));
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadFromJsonAsync<FaultBody>(cancellationToken) ?? throw new InvalidDataException("The application answered a fault switch with nothing.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            using (logger.BeginScope(Keys(correlationId, name, "Failed", "FaultOwnerUnreachable")))
            {
                LogNotSwitched(exception, name);
            }

            throw;
        }

        using (logger.BeginScope(Keys(correlationId, name, "Succeeded", failureCode: null)))
        {
            LogSwitched(name, body.On);
        }

        return body.ToView(owner);
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, FaultOwnerOptions owner, string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(owner.Address!, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(owner, cancellationToken));
        return request;
    }

    private static Dictionary<string, object?> Keys(string correlationId, string fault, string outcome, string? failureCode)
    {
        var keys = new Dictionary<string, object?> { ["correlationId"] = correlationId, ["flow"] = Flow, ["fault"] = fault, ["outcome"] = outcome };
        if (failureCode is not null)
        {
            keys["failureCode"] = failureCode;
        }

        return keys;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Switched the fault {Fault} on: {On}")]
    private partial void LogSwitched(string fault, bool on);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switching the fault {Fault} failed")]
    private partial void LogNotSwitched(Exception exception, string fault);

    private sealed record SwitchBody(bool On, int? DurationSeconds);

    private sealed record FaultBody(string Name, string Owner, string Description, bool On, DateTimeOffset? Until)
    {
        // The owner is the name the portal knows the application by, not what the application calls itself.
        public FaultView ToView(string owner) => new(Name, owner, Description, On, Until);
    }
}
