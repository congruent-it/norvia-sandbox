using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceOutcomeWorker;

/// <summary>How the flow's run ended: <see cref="Done"/>, or the way it failed, as the workflow's run would fail.</summary>
internal enum CarrierInvoiceOutcomeFlowOutcome
{
    /// <summary>The outcome was sent and reported told, or the Carrier didn't acknowledge it and it was reported not delivered.</summary>
    Done,

    /// <summary>The message isn't a <c>CarrierInvoiceApproved</c> the Carrier can be told, so nothing was sent.</summary>
    Unreadable,

    /// <summary>The ERP didn't answer the report. The invoice shows neither a told time nor the flag.</summary>
    ErpUnavailable,

    /// <summary>The ERP refused the report, which sending it again won't change.</summary>
    ErpRefused,
}

/// <summary>
/// The E2 Carrier invoice outcome Logic App's steps, played locally (ADR 0033, ADR 0043): map the approved Carrier invoice, send the outcome to the Carrier's invoice outcome
/// endpoint under the bounded exponential retry policy, and report the outcome to the ERP under the Carrier invoice's correlation ID: told when
/// the Carrier acknowledged it, not delivered with the technical cause when the retries are used up or the Carrier refused it. When the ERP
/// can't be reached the run fails and the invoice shows neither a told time nor the flag.
/// </summary>
internal sealed partial class CarrierInvoiceOutcomeFlow(
    CarrierEndpoint carrier,
    ErpReports erp,
    IOptions<CarrierInvoiceOutcomeWorkerOptions> options,
    TimeProvider time,
    ILogger<CarrierInvoiceOutcomeFlow> logger)
{
    public async Task<CarrierInvoiceOutcomeFlowOutcome> RunAsync(string message, CancellationToken cancellationToken)
    {
        var request = RequestOf(message);
        var document = CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(message);
        if (request is null || document is null)
        {
            using var unreadable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "CarrierInvoiceApprovedUnreadable"));
            LogUnreadable();
            return CarrierInvoiceOutcomeFlowOutcome.Unreadable;
        }

        using var trail = logger.BeginScope(request.Keys);
        var answer = await SendAsync(request, document.ToJsonString(), cancellationToken);
        if (!answer.Succeeded)
        {
            var failure = answer.IsRetryable ? "CarrierUnavailable" : "CarrierRefused";
            using (logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, failure)))
            {
                LogNotAcknowledged(answer.StatusCode);
            }

            return await ReportAsync(
                token => erp.ReportNotDeliveredAsync(request.CarrierId, request.DocumentNumber, request.CorrelationId, answer.StatusCode, answer.Message, token),
                "not delivered",
                cancellationToken);
        }

        return await ReportAsync(token => erp.ReportToldAsync(request.CarrierId, request.DocumentNumber, request.CorrelationId, token), "told", cancellationToken);
    }

    private async Task<CarrierInvoiceOutcomeFlowOutcome> ReportAsync(Func<CancellationToken, Task<int>> report, string what, CancellationToken cancellationToken)
    {
        var status = await report(cancellationToken);
        using var reported = logger.BeginScope(
            status is >= 200 and < 300
                ? TrackingKeys.OutcomeOf(TrackingKeys.Succeeded)
                : TrackingKeys.OutcomeOf(TrackingKeys.Failed, status is 0 or 408 or 429 or >= 500 ? "ErpUnavailable" : "ErpCallRefused"));
        LogReported(what, status);
        return status switch
        {
            >= 200 and < 300 => CarrierInvoiceOutcomeFlowOutcome.Done,
            0 or 408 or 429 or >= 500 => CarrierInvoiceOutcomeFlowOutcome.ErpUnavailable,
            _ => CarrierInvoiceOutcomeFlowOutcome.ErpRefused,
        };
    }

    private async Task<CarrierAnswer> SendAsync(Request request, string document, CancellationToken cancellationToken)
    {
        if (!options.Value.Carriers.TryGetValue(request.CarrierId, out var endpoint))
        {
            return new(0, $"No invoice outcome endpoint is configured for Carrier {request.CarrierId}.");
        }

        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await carrier.SendAsync(endpoint, document, request.CorrelationId, cancellationToken);
            if (answer.Succeeded || !answer.IsRetryable || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, "CarrierUnavailable"));
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
                || Text(issued, "invoiceNumber") is not { } documentNumber
                || Text(issued, "carrierId") is not { } carrierId)
            {
                return null;
            }

            return new(correlationId, documentNumber, carrierId, Text(issued, "eventId"));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The message isn't an approved Carrier invoice")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Carrier's endpoint answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Carrier's endpoint didn't acknowledge the outcome (status {StatusCode}); reporting it not delivered")]
    private partial void LogNotAcknowledged(int statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reported the outcome {Report} to the ERP, which answered {StatusCode}")]
    private partial void LogReported(string report, int statusCode);

    /// <summary>What a request names, and the tracking contract's keys it gives the trail.</summary>
    private sealed record Request(string CorrelationId, string DocumentNumber, string CarrierId, string? EventId)
    {
        public Dictionary<string, object?> Keys
        {
            get
            {
                var keys = new Dictionary<string, object?>
                {
                    [TrackingKeys.CorrelationId] = CorrelationId,
                    [TrackingKeys.Flow] = TrackingKeys.CarrierInvoices,
                    [TrackingKeys.PartyId] = CarrierId,
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
