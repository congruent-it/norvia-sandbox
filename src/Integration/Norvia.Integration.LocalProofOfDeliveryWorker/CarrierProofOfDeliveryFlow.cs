using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Integration.CarrierBooking;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>How the flow's run ended, and so what happens to the queue message: the Logic App's settling steps.</summary>
/// <param name="DeadLetterReason">The dead-letter reason when <see cref="ProofOfDeliverySettlement.DeadLetter"/>.</param>
/// <param name="DeadLetterDescription">The dead-letter error description when <see cref="ProofOfDeliverySettlement.DeadLetter"/>.</param>
internal sealed record ProofOfDeliveryRun(ProofOfDeliverySettlement Settlement, string? DeadLetterReason = null, string? DeadLetterDescription = null)
{
    /// <summary>True when the workflow's run would end Failed: the Proof of delivery wasn't recorded.</summary>
    public bool RunFailed => Settlement != ProofOfDeliverySettlement.Complete;
}

/// <summary>
/// The Carrier Proof of delivery Logic App's steps, played locally (ADR 0032): fetch the claim check's blob, map it,
/// record it with the TMS under the message's correlation ID, delete the blob once the TMS accepted it, and settle the
/// queue message by what the TMS answered. A blob the TMS didn't accept is kept for investigation.
/// </summary>
internal sealed partial class CarrierProofOfDeliveryFlow(IInboundProofsOfDelivery inbound, TmsProofsOfDelivery tms, ILogger<CarrierProofOfDeliveryFlow> logger)
{
    /// <summary>The dead-letter reason, and failure code, of a message whose blob can't be read.</summary>
    internal const string ProofOfDeliveryBlobMissing = "ProofOfDeliveryBlobMissing";

    /// <summary>The dead-letter reason, and failure code, of a Proof of delivery the map can't read.</summary>
    internal const string UnmappableProofOfDelivery = "UnmappableProofOfDelivery";

    /// <param name="body">The queue message's body: the blob's reference as JSON.</param>
    /// <param name="properties">The message's application properties, which API Management set.</param>
    public async Task<ProofOfDeliveryRun> RunAsync(string body, IReadOnlyDictionary<string, object> properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(properties);
        var correlationId = Property(properties, TrackingKeys.CorrelationId) ?? Guid.NewGuid().ToString();
        var carrierId = Property(properties, TrackingKeys.PartyId);
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = TrackingKeys.CarrierProofOfDelivery,
        };
        foreach (var name in new[] { TrackingKeys.PartyId, TrackingKeys.SourceMessageId })
        {
            if (Property(properties, name) is { } value)
            {
                keys[name] = value;
            }
        }

        using var trail = logger.BeginScope(keys);

        var blobName = BlobNameOf(body);
        var content = blobName is null ? null : await inbound.ReadAsync(blobName, cancellationToken);
        if (blobName is null || content is null)
        {
            const string Description = "The Proof of delivery's blob could not be read from the inbound container.";
            using var missing = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, ProofOfDeliveryBlobMissing));
            LogBlobMissing();
            return new(ProofOfDeliverySettlement.DeadLetter, ProofOfDeliveryBlobMissing, Description);
        }

        MappedProofOfDelivery mapped;
        try
        {
            // The Carrier comes from the message's partyId, which API Management set from the token; without one nothing can be mapped.
            mapped = CarrierProofOfDeliveryMap.Map(content, carrierId ?? string.Empty);
        }
        catch (Exception exception) when (exception is InvalidProofOfDeliveryException or ArgumentException)
        {
            const string Description = "The Carrier's Proof of delivery could not be mapped: its content is not a Proof of delivery the map can read.";
            using var unmappable = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.DeadLettered, UnmappableProofOfDelivery));
            LogUnmappable(exception);
            return new(ProofOfDeliverySettlement.DeadLetter, UnmappableProofOfDelivery, Description);
        }

        var (status, answer) = await tms.RecordAsync(mapped, correlationId, cancellationToken);
        var outcome = TmsProofOfDeliveryOutcome.Of(status, answer);

        var recorded = TrackingKeys.OutcomeOf(outcome.Outcome, outcome.FailureCode);
        if (outcome.ShipmentNumber is not null)
        {
            recorded[TrackingKeys.ShipmentNumber] = outcome.ShipmentNumber;
        }

        if (mapped.Metadata["carrierBookingNumber"] is JsonValue number && number.TryGetValue<string>(out var bookingNumber))
        {
            recorded[TrackingKeys.CarrierBookingNumber] = bookingNumber;
        }

        using var tmsCall = logger.BeginScope(recorded);
        LogRecorded(status, outcome.Settlement);
        switch (outcome.Settlement)
        {
            case ProofOfDeliverySettlement.Complete:
                return await DeleteTheBlobAsync(blobName, cancellationToken);
            case ProofOfDeliverySettlement.DeadLetter:
                return new(ProofOfDeliverySettlement.DeadLetter, outcome.FailureCode, outcome.Description);
            default:
                return new(outcome.Settlement);
        }
    }

    // The blob is deleted once the TMS has the Proof of delivery. When it can't be, the message goes back on the queue: the
    // TMS answers the resent Proof of delivery as a repeat, which completes it, and the blob is deleted then. The trail
    // keeps the TMS's outcome, Succeeded: the Proof of delivery is recorded.
    private async Task<ProofOfDeliveryRun> DeleteTheBlobAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            await inbound.DeleteAsync(blobName, cancellationToken);
            return new(ProofOfDeliverySettlement.Complete);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotDeleted(exception);
            return new(ProofOfDeliverySettlement.Retry);
        }
    }

    // The blob a reference names, when the body is a JSON object that has one as text.
    private static string? BlobNameOf(string body)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject reference && reference["blobName"] is JsonValue value && value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Property(IReadOnlyDictionary<string, object> properties, string name) =>
        properties.TryGetValue(name, out var value) && value?.ToString() is { Length: > 0 } text ? text : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Proof of delivery's blob can't be read; the message is dead-lettered and the run fails")]
    private partial void LogBlobMissing();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Proof of delivery can't be mapped; the message is dead-lettered and the run fails")]
    private partial void LogUnmappable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The TMS answered {StatusCode}; the message is settled as {Settlement}")]
    private partial void LogRecorded(int statusCode, ProofOfDeliverySettlement settlement);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS has the Proof of delivery, but its blob couldn't be deleted; the message goes back on the queue")]
    private partial void LogNotDeleted(Exception exception);
}
