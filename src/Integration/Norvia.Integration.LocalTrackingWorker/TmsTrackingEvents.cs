using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalTrackingWorker;

/// <summary>
/// Records a Tracking event with the TMS as the Carrier-tracking Logic App does: the Canonical Model event, the
/// message's correlation ID and a token of the one identity holding the record role. It does not retry: a server error or
/// timeout is retried from the queue, up to its maximum delivery count.
/// </summary>
internal sealed partial class TmsTrackingEvents(HttpClient http, IOptions<TrackingWorkerOptions> options, TimeProvider time, ILogger<TmsTrackingEvents> logger)
{
    private const string RecordRole = "TrackingEvents.Record";

    /// <summary>The TMS's status and answer, or 0 and empty when it didn't answer.</summary>
    public async Task<(int StatusCode, string Body)> RecordAsync(string trackingEvent, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/tracking-events", UriKind.Relative))
            {
                Content = new StringContent(trackingEvent, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RecordToken());

            using var response = await http.SendAsync(request, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return (0, string.Empty);
        }
    }

    // A token as Entra issues it to the Carrier-tracking Logic App's managed identity.
    private string RecordToken()
    {
        var tokens = options.Value.TmsTokens;
        var now = time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tokens.Issuer,
            Audience = tokens.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["azp"] = tokens.ClientId, ["roles"] = new[] { RecordRole } },
            SigningCredentials = new SigningCredentials(tokens.Key, SecurityAlgorithms.HmacSha256),
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}
