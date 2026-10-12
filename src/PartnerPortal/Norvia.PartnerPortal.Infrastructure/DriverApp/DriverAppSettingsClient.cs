using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.DriverAppFault;

namespace Norvia.PartnerPortal.Infrastructure.DriverApp;

/// <summary>Reads and changes the Driver app's demo settings through its API.</summary>
internal sealed partial class DriverAppSettingsClient(HttpClient http, PortalActivities activities, ILogger<DriverAppSettingsClient> logger) : IDriverAppSettings
{
    private const string Path = "v1/settings/unavailable";
    private const string Flow = "driver-app-fault";

    public async Task<bool> IsUnavailableAsync(CancellationToken cancellationToken)
    {
        var setting = await http.GetFromJsonAsync<UnavailableBody>(Path, cancellationToken);
        return setting?.Unavailable ?? false;
    }

    public async Task SetUnavailableAsync(bool unavailable, CancellationToken cancellationToken)
    {
        // The presenter panel sends a command to the Driver app, so it does it in a span of its own (coding guidelines §7).
        using var span = activities.Source.StartActivity("Switch Driver app fault", ActivityKind.Internal);
        var correlationId = Guid.NewGuid().ToString();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, Path) { Content = JsonContent.Create(new UnavailableBody(unavailable)) };
            request.Headers.Add("X-Correlation-ID", correlationId);
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            using (logger.BeginScope(Keys(correlationId, "Failed", "DriverAppUnreachable")))
            {
                LogNotSwitched(exception, unavailable);
            }

            throw;
        }

        using (logger.BeginScope(Keys(correlationId, "Succeeded", failureCode: null)))
        {
            LogSwitched(unavailable);
        }
    }

    private static Dictionary<string, object?> Keys(string correlationId, string outcome, string? failureCode)
    {
        var keys = new Dictionary<string, object?> { ["correlationId"] = correlationId, ["flow"] = Flow, ["outcome"] = outcome };
        if (failureCode is not null)
        {
            keys["failureCode"] = failureCode;
        }

        return keys;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Switched the Driver app unavailable fault to {Unavailable}")]
    private partial void LogSwitched(bool unavailable);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switching the Driver app unavailable fault to {Unavailable} failed")]
    private partial void LogNotSwitched(Exception exception, bool unavailable);

    private sealed record UnavailableBody(bool Unavailable);
}
