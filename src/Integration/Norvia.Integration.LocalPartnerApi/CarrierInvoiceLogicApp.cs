using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// Calls the Carrier invoice Logic App as API Management does: the invoice as the Carrier sent it, the Carrier and the correlation ID
/// as headers, and a token of the one identity allowed to call the trigger. The workflow retries the ERP and answers the Carrier at once,
/// so this client neither retries nor gives up early.
/// </summary>
internal sealed partial class CarrierInvoiceLogicApp(HttpClient http, IOptions<CarrierInvoiceOptions> options, TimeProvider time, ILogger<CarrierInvoiceLogicApp> logger)
{
    private const string TriggerRole = "CarrierInvoice.Trigger";

    /// <summary>The workflow's status and answer, or 0 and empty when it didn't answer.</summary>
    public async Task<(int StatusCode, string Body)> SubmitAsync(string carrierInvoice, string carrierId, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/carrier-invoice", UriKind.Relative))
            {
                Content = new StringContent(carrierInvoice, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Carrier-ID", carrierId);
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.Tokens, time, options.Value.Tokens.ClientId!, TriggerRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogLogicAppUnreachable(exception);
            return (0, string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Carrier invoice Logic App couldn't be reached")]
    private partial void LogLogicAppUnreachable(Exception exception);
}
