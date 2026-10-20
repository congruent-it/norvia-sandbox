using Microsoft.Extensions.Options;
using Norvia.Integration.CarrierBooking;
using Norvia.Integration.TransportOrderIntake;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceWorker;

/// <summary>
/// The Carrier invoice Logic App's steps, played locally: map the Carrier's invoice into the Canonical Model, post it to the ERP billing API
/// as the one identity holding the receive role under the bounded exponential retry policy, and relay the ERP's answer to the Carrier at once.
/// </summary>
internal sealed partial class CarrierInvoiceFlow(ErpCarrierInvoices erp, IOptions<CarrierInvoiceWorkerOptions> options, TimeProvider time, ILogger<CarrierInvoiceFlow> logger)
{
    /// <param name="carrierInvoice">The Carrier's invoice as it came.</param>
    /// <param name="carrierId">The Carrier API Management took from the token (ADR 0005).</param>
    /// <param name="correlationId">The correlation ID API Management created.</param>
    public async Task<BookingResponse> RunAsync(string carrierInvoice, string carrierId, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carrierInvoice);
        var mapped = CarrierInvoiceMap.ToCarrierInvoiceSubmission(carrierInvoice, carrierId, correlationId);

        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.CarrierInvoice,
            [TrackingKeys.PartyId] = carrierId,
        };
        if (mapped.InvoiceNumber is { } documentNumber)
        {
            keys[TrackingKeys.DocumentNumber] = documentNumber;
        }

        using var trail = logger.BeginScope(keys);
        var (statusCode, body) = (0, string.Empty);
        if (mapped.ErpRequest is { } submission)
        {
            (statusCode, body) = await SendAsync(submission.ToJsonString(), correlationId, cancellationToken);
            var (outcome, failureCode) = ErpInvoiceOutcome.Of(statusCode);
            using var erpCall = logger.BeginScope(TrackingKeys.OutcomeOf(outcome, failureCode));
            LogErpAnswered(statusCode);
        }

        var answer = CarrierInvoiceResponses.Relay(mapped, statusCode, body, correlationId);
        if (answer.FailureCode is { } failed)
        {
            // The run fails for these in Azure: the Carrier has been told, but the invoice was not received.
            using var failedRun = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, failed));
            LogNotReceived(answer.StatusCode);
        }
        else
        {
            using var answered = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
            LogAnswered(answer.StatusCode);
        }

        return answer;
    }

    private async Task<(int StatusCode, string Body)> SendAsync(string submission, string correlationId, CancellationToken cancellationToken)
    {
        var retry = options.Value.Retry;
        for (var attempt = 0; ; attempt++)
        {
            var answer = await erp.ReceiveAsync(submission, correlationId, cancellationToken);
            if (!HttpStatus.IsTransient(answer.StatusCode) || attempt >= retry.Count)
            {
                return answer;
            }

            var wait = TimeSpan.FromTicks(Math.Min(retry.Interval.Ticks * (1L << attempt), retry.MaximumInterval.Ticks));
            using var retrying = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Retrying, "ErpUnavailable"));
            LogRetrying(answer.StatusCode, attempt + 1, retry.Count, wait);
            await Task.Delay(wait, time, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The ERP billing API answered {StatusCode}")]
    private partial void LogErpAnswered(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ERP billing API answered {StatusCode}; retry {Attempt} of {Count} in {Wait}")]
    private partial void LogRetrying(int statusCode, int attempt, int count, TimeSpan wait);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the Carrier with {StatusCode}")]
    private partial void LogAnswered(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invoice was not received; answered the Carrier with {StatusCode}")]
    private partial void LogNotReceived(int statusCode);
}
