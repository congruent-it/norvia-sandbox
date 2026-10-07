using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// Calls the TMS Shipper tracking API as API Management does (P8): the Shipper ID API Management took from the token, the
/// correlation ID it created, and a token of the one identity holding the Shipper tracking role. A short wait and no retry,
/// so a failure reaches the Shipper at once; unlike the Logic Apps' calls, this client has no resilience handler.
/// </summary>
internal sealed partial class TmsShipperTracking(HttpClient http, IOptions<TmsOptions> options, TimeProvider time, ILogger<TmsShipperTracking> logger)
{
    /// <summary>How long the policy waits for the TMS: forward-request's timeout.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string ReadRole = "ShipperTracking.Read";

    /// <param name="pathAndQuery">The TMS route and the query string as the Shipper sent it.</param>
    public async Task<TmsAnswer> QueryAsync(string pathAndQuery, string shipperId, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(pathAndQuery, UriKind.Relative));
            request.Headers.Add("X-Shipper-ID", shipperId);
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.Tokens, time, options.Value.ShipperTrackingClientId!, ReadRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return new TmsAnswer(
                (int)response.StatusCode,
                await response.Content.ReadAsByteArrayAsync(cancellationToken),
                response.Content.Headers.ContentType?.ToString(),
                response.Content.Headers.TryGetValues("Content-Disposition", out var disposition) ? disposition.FirstOrDefault() : null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return TmsAnswer.Unreachable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached, or didn't answer in time")]
    private partial void LogTmsUnreachable(Exception exception);
}
