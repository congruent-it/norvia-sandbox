using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>
/// Records a Proof of delivery with the TMS as the Carrier Proof of delivery Logic App does: one multipart call with the
/// Canonical Model metadata and the files, the message's correlation ID and a token of the one identity holding the
/// record role. It does not retry: a server error or timeout is retried from the queue, up to its maximum delivery count.
/// </summary>
internal sealed partial class TmsProofsOfDelivery(HttpClient http, IOptions<ProofOfDeliveryWorkerOptions> options, TimeProvider time, ILogger<TmsProofsOfDelivery> logger)
{
    private const string RecordRole = "ProofsOfDelivery.Record";

    /// <summary>The TMS's status and answer, or 0 and empty when it didn't answer.</summary>
    public async Task<(int StatusCode, string Body)> RecordAsync(MappedProofOfDelivery proofOfDelivery, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proofOfDelivery);
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(proofOfDelivery.Metadata.ToJsonString(), Encoding.UTF8, "application/json"), "metadata");
            foreach (var file in proofOfDelivery.Files)
            {
                var part = new ByteArrayContent(file.Content);
                part.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
                form.Add(part, "files", file.Name);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/proofs-of-delivery", UriKind.Relative)) { Content = form };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(options.Value.TmsTokens, time, options.Value.TmsTokens.ClientId!, RecordRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return (0, string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}
