using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalConsigneeNotificationWorker;

/// <summary>
/// Asks the TMS for a Transport Order's Consignee address and tracking code, as the Consignee notification Logic App does: the action's
/// correlation ID and a token of the one identity holding the read role. It doesn't retry: the flow does, under the workflow's policy.
/// </summary>
internal sealed partial class TmsConsigneeNotifications(HttpClient http, IOptions<ConsigneeNotificationWorkerOptions> options, TimeProvider time, ILogger<TmsConsigneeNotifications> logger)
{
    private const string ReadRole = "ConsigneeNotifications.Read";

    /// <summary>The TMS's status and body, or 0 and nothing when it didn't answer. The body holds the address and the code: nothing logs it.</summary>
    public async Task<TmsAnswer> GetAsync(string transportOrderNumber, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/v1/consignee-notifications/{Uri.EscapeDataString(transportOrderNumber)}", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TmsTokens.Create(options.Value.TmsTokens, time, options.Value.TmsTokens.ClientId!, ReadRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return new((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return new(0, string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}

/// <param name="StatusCode">The HTTP status, or 0 when the TMS didn't answer.</param>
/// <param name="Body">What the TMS said: the address and the tracking code on a success.</param>
internal sealed record TmsAnswer(int StatusCode, string Body)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;

    /// <summary>A Transport Order with no usable email, no tracking code, or no record at all: there is nothing to send, and that is no error.</summary>
    public bool NothingToSend => StatusCode == 404;

    /// <summary>A server error, a timeout or no answer, as the workflow's policy retries.</summary>
    public bool IsRetryable => StatusCode is 0 or 408 or 429 or >= 500;

    // The body holds the address and the code.
    public override string ToString() => $"TmsAnswer {StatusCode}";
}
