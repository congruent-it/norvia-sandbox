using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalShipperInvoiceWorker;

/// <summary>
/// Reports an invoice as sent to the ERP as the Shipper invoice Logic App does: the invoice's correlation ID and a token of the one
/// identity holding the report role. The standard resilience handler retries it.
/// </summary>
internal sealed partial class ErpReports(HttpClient http, IOptions<ShipperInvoiceWorkerOptions> options, TimeProvider time, ILogger<ErpReports> logger)
{
    private const string ReportRole = "ShipperInvoices.Report";

    /// <summary>The ERP's status, or 0 when it didn't answer.</summary>
    public Task<int> ReportSentAsync(string documentNumber, string correlationId, CancellationToken cancellationToken) =>
        PostAsync(documentNumber, "report-sent", correlationId, null, cancellationToken);

    /// <summary>Reports the invoice Not delivered with the endpoint's status (null when it didn't answer) and the technical cause. The ERP's status, or 0 when it didn't answer.</summary>
    public Task<int> ReportNotDeliveredAsync(
        string documentNumber, string correlationId, int statusCode, string message, CancellationToken cancellationToken) =>
        PostAsync(
            documentNumber, "report-not-delivered", correlationId,
            JsonContent.Create(new { statusCode = statusCode == 0 ? (int?)null : statusCode, message }), cancellationToken);

    private async Task<int> PostAsync(string documentNumber, string report, string correlationId, HttpContent? content, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri($"/v1/shipper-invoices/{Uri.EscapeDataString(documentNumber)}/{report}", UriKind.Relative))
            {
                Content = content,
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.ErpTokens, time, options.Value.ErpTokens.ClientId!, ReportRole));

            using var response = await http.SendAsync(request, cancellationToken);
            return (int)response.StatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogErpUnreachable(exception);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP couldn't be reached")]
    private partial void LogErpUnreachable(Exception exception);
}
