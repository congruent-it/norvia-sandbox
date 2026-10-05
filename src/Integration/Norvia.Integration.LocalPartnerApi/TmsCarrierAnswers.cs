using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// Calls the TMS Carrier booking API's answer operation as the Carrier-answer Logic App does: the Canonical Model answer,
/// the answer's correlation ID, and a token of the one identity holding the answer role. The standard resilience handler
/// retries it, as the workflow's retry policy does in Azure; the TMS answers a repeated answer as before.
/// </summary>
internal sealed partial class TmsCarrierAnswers(HttpClient http, IOptions<TmsOptions> options, TimeProvider time, ILogger<TmsCarrierAnswers> logger)
{
    private const string AnswerRole = "CarrierBookings.Answer";

    /// <summary>The TMS's status and answer, or 0 and empty when it didn't answer, as the relay takes them.</summary>
    public async Task<(int StatusCode, string Body)> AnswerAsync(string carrierBookingNumber, string answer, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{Uri.EscapeDataString(carrierBookingNumber)}/answer", UriKind.Relative))
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.Tokens, time, options.Value.AnswerClientId!, AnswerRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return (0, string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}
