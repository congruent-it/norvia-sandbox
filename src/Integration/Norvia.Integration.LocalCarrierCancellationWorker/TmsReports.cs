using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierCancellationWorker;

/// <summary>
/// Reports a cancellation as told, or as not delivered, to the TMS as the Carrier cancellation Logic App does: the action's correlation
/// ID and a token of the one identity holding the report role. The standard resilience handler retries it.
/// </summary>
internal sealed partial class TmsReports(HttpClient http, IOptions<CarrierCancellationWorkerOptions> options, TimeProvider time, ILogger<TmsReports> logger)
{
    private const string ReportRole = "CarrierBookings.Report";

    /// <summary>The TMS's status, or 0 when it didn't answer.</summary>
    public Task<int> ReportToldAsync(string carrierBookingNumber, string correlationId, CancellationToken cancellationToken) =>
        PostAsync(carrierBookingNumber, "report-cancellation-told", correlationId, content: null, cancellationToken);

    public Task<int> ReportNotDeliveredAsync(
        string carrierBookingNumber, string correlationId, int statusCode, string message, CancellationToken cancellationToken) =>
        PostAsync(carrierBookingNumber, "report-cancellation-not-delivered", correlationId, JsonContent.Create(new { statusCode, message }), cancellationToken);

    private async Task<int> PostAsync(string carrierBookingNumber, string report, string correlationId, HttpContent? content, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri($"/v1/carrier-bookings/{Uri.EscapeDataString(carrierBookingNumber)}/{report}", UriKind.Relative))
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
