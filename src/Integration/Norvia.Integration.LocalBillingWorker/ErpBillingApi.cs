using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalBillingWorker;

/// <summary>What the ERP billing API answered: the status (zero for no answer) and, for a refusal, its stable code and message.</summary>
internal sealed record ErpAnswer(int StatusCode, string? Code = null, string? Message = null)
{
    public bool Succeeded => HttpStatus.IsSuccess(StatusCode);

    public bool IsRetryable => HttpStatus.IsTransient(StatusCode);

    /// <summary>The workflow's failure code: unavailable for an unanswered or transient call, else the ERP's own code.</summary>
    public string? FailureCode => Succeeded ? null : IsRetryable ? BillingFailureCodes.ErpUnavailable : Code ?? BillingFailureCodes.ErpCallRefused;

    public string Description => Message
        ?? (IsRetryable ? "The ERP billing API did not answer after the retries." : "The ERP billing API refused the Billable line.");
}

/// <summary>
/// Records a Billable line with the ERP billing API as the Billing Logic App does: the event as it is, the event's correlation ID and a
/// token of the one identity holding the record role.
/// </summary>
internal sealed partial class ErpBillingApi(HttpClient http, IOptions<BillingWorkerOptions> options, TimeProvider time, ILogger<ErpBillingApi> logger)
{
    private const string RecordRole = "BillableLines.Record";

    public async Task<ErpAnswer> RecordAsync(string transportOrderBillable, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/billable-lines", UriKind.Relative))
            {
                Content = new StringContent(transportOrderBillable, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", TmsTokens.Create(options.Value.ErpTokens, time, options.Value.ErpTokens.ClientId!, RecordRole));

            using var response = await http.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            if (HttpStatus.IsSuccess(status))
            {
                return new(status);
            }

            var problem = Problem(await response.Content.ReadAsStringAsync(cancellationToken));
            return new(status, Text(problem, "code"), Text(problem, "detail") ?? Text(problem, "title"));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogErpUnreachable(exception);
            return new(0);
        }
    }

    private static JsonObject? Problem(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP billing API couldn't be reached")]
    private partial void LogErpUnreachable(Exception exception);
}
