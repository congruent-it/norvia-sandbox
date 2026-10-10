using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>
/// Reports a Trip version as delivered, or as not delivered, to the TMS as the Trip dispatch Logic App does: the action's
/// correlation ID and a token of the one identity holding the report role.
/// </summary>
internal sealed partial class TmsReports(HttpClient http, IOptions<TripDispatchWorkerOptions> options, TimeProvider time, ILogger<TmsReports> logger)
{
    private const string ReportRole = "Trips.ReportDelivery";

    /// <summary>The TMS's status, or 0 when it didn't answer.</summary>
    public Task<int> ReportDeliveredAsync(string tripNumber, int version, string correlationId, CancellationToken cancellationToken) =>
        PostAsync(tripNumber, version, "report-delivered", correlationId, content: null, cancellationToken);

    public Task<int> ReportNotDeliveredAsync(string tripNumber, int version, string correlationId, string cause, CancellationToken cancellationToken) =>
        PostAsync(tripNumber, version, "report-not-delivered", correlationId, JsonContent.Create(new { cause }), cancellationToken);

    private async Task<int> PostAsync(string tripNumber, int version, string report, string correlationId, HttpContent? content, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri($"/v1/trips/{Uri.EscapeDataString(tripNumber)}/versions/{version}/{report}", UriKind.Relative))
            {
                Content = content,
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(options.Value.TmsTokens, time, options.Value.TmsTokens.ClientId!, ReportRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return (int)response.StatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}
