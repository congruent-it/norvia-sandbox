using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalShipperInvoiceWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum ShipperInvoiceFlowOutcome
{
    /// <summary>The document was sent and reported Sent, or the Shipper didn't acknowledge it and it was reported Not delivered.</summary>
    Done,

    /// <summary>The message isn't a <c>ShipperInvoiceIssued</c> or <c>CreditNoteIssued</c> the Shipper can be sent, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The ERP didn't answer the report. The document stays Issued without a sent time.</summary>
    ErpUnavailable,

    /// <summary>The ERP refused the report, which sending it again won't change.</summary>
    ErpRefused,
}

/// <summary>
/// The E1 Shipper invoice Logic App's steps, played locally (ADR 0033, ADR 0043): map the invoice or Credit note, send it to the Shipper's invoice
/// endpoint under the bounded exponential retry policy, and report the outcome to the ERP under the document's correlation ID: Sent when
/// the Shipper acknowledged it, Not delivered with the technical cause when the retries are used up or the Shipper refused it. When the ERP
/// can't be reached the run fails and the document stays Issued without a sent time.
/// </summary>
internal sealed partial class ShipperInvoiceFlow(
    ShipperEndpoint shipper,
    ErpReports erp,
    IOptions<ShipperInvoiceWorkerOptions> options,
    TimeProvider time,
    ILogger<ShipperInvoiceFlow> logger)
{
    public async Task<ShipperInvoiceFlowOutcome> RunAsync(string message, CancellationToken cancellationToken)
    {
        var request = RequestOf(message);
        var document = ShipperInvoiceMap.ToShipperDocument(message);
        if (request is null || document is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ShipperInvoiceIssuedUnreadable"));
            LogUnreadable();
            return ShipperInvoiceFlowOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(request.Keys);
        var answer = await SendAsync(request, document.ToJsonString(), cancellationToken);
        if (!answer.Succeeded)
        {
            var failure = answer.IsRetryable ? "ShipperUnavailable" : "ShipperRefused";
            using (logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, failure)))
            {
                LogNotAcknowledged(answer.StatusCode);
            }

            return await ReportAsync(
                token => erp.ReportNotDeliveredAsync(request.DocumentNumber, request.CorrelationId, answer.StatusCode, answer.Message, token),
                "Not delivered",
                cancellationToken);
        }

        return await ReportAsync(token => erp.ReportSentAsync(request.DocumentNumber, request.CorrelationId, token), "Sent", cancellationToken);
    }

    private async Task<ShipperInvoiceFlowOutcome> ReportAsync(Func<CancellationToken, Task<int>> report, string what, CancellationToken cancellationToken)
    {
        var status = await report(cancellationToken);
        using var reported = logger.BeginScope(
            status is >= 200 and < 300
                ? TrackingKeys.OutcomeOf(TrackingKeys.Succeeded)
                : TrackingKeys.OutcomeOf(TrackingKeys.Failed, status is 0 or 408 or 429 or >= 500 ? "ErpUnavailable" : "ErpCallRefused"));
        LogReported(what, status);
        return status switch
        {
            >= 200 and < 300 => ShipperInvoiceFlowOutcome.Done,
            0 or 408 or 429 or >= 500 => ShipperInvoiceFlowOutcome.ErpUnavailable,
            _ => ShipperInvoiceFlowOutcome.ErpRefused,
        };
    }

    private async Task<ShipperAnswer> SendAsync(Request request, string document, CancellationToken cancellationToken)
    {
        if (!options.Value.Shippers.TryGetValue(request.ShipperId, out var endpoint))
        {
            return new(0, $"No invoice endpoint is configured for Shipper {request.ShipperId}.");
        }

        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await shipper.SendAsync(endpoint, document, request.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, "ShipperUnavailable"));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    private static Request? RequestOf(string message)
    {
        try
        {
            if (JsonNode.Parse(message) is not JsonObject issued
                || Text(issued, "correlationId") is not { } correlationId
                || (issued["invoice"] ?? issued["creditNote"]) is not JsonObject document
                || (Text(document, "creditNoteNumber") ?? Text(document, "invoiceNumber")) is not { } documentNumber
                || document["shipper"] is not JsonObject shipper
                || Text(shipper, "shipperId") is not { } shipperId)
            {
                return null;
            }

            return new(correlationId, documentNumber, shipperId, Text(issued, "eventId"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't an issued Shipper invoice or Credit note")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Shipper's endpoint answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Shipper's endpoint didn't acknowledge the document (status {StatusCode}); reporting it Not delivered")]
    private partial void LogNotAcknowledged(int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported the document {Report} to the ERP, which answered {StatusCode}")]
    private partial void LogReported(string report, int statusCode);

    /// <summary>What a request names, and the tracking contract's keys it gives the trail.</summary>
    private sealed record Request(string CorrelationId, string DocumentNumber, string ShipperId, string? EventId)
    {
        public Dictionary<string, object?> Keys
        {
            get
            {
                var keys = new Dictionary<string, object?>
                {
                    [TrackingKeys.CorrelationId] = CorrelationId,
                    [TrackingKeys.Flow] = TrackingKeys.Invoicing,
                    [TrackingKeys.PartyId] = ShipperId,
                    [TrackingKeys.DocumentNumber] = DocumentNumber,
                };
                if (EventId is not null)
                {
                    keys[TrackingKeys.SourceMessageId] = EventId;
                }

                return keys;
            }
        }
    }
}
