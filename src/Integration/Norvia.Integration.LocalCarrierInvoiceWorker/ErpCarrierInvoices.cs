using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceWorker;

/// <summary>
/// Posts the Canonical Model Carrier invoice to the ERP billing API as the Carrier invoice Logic App does: the invoice's correlation ID
/// and a token of the one identity holding the receive role.
/// </summary>
internal sealed partial class ErpCarrierInvoices(HttpClient http, IOptions<CarrierInvoiceWorkerOptions> options, TimeProvider time, ILogger<ErpCarrierInvoices> logger)
{
    private const string ReceiveRole = "CarrierInvoices.Receive";

    /// <summary>The ERP's status and answer, or 0 and empty when it didn't answer, as the relay takes them.</summary>
    public async Task<(int StatusCode, string Body)> ReceiveAsync(string submission, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/carrier-invoices", UriKind.Relative))
            {
                Content = new StringContent(submission, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.ErpTokens, time, options.Value.ErpTokens.ClientId!, ReceiveRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogErpUnreachable(exception);
            return (0, string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP couldn't be reached")]
    private partial void LogErpUnreachable(Exception exception);
}
