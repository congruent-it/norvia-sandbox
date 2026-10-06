namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// What the TMS's answer to a Proof of delivery means for the Carrier Proof of delivery Logic App (P6): how the queue
/// message is settled and what the tracking contract's <c>outcome</c> and <c>failureCode</c> are (coding guidelines §7).
/// One place, for the workflow's definition and for the local worker that plays it, so a local trail reads as one in Azure.
/// </summary>
/// <param name="FailureCode">The dead-letter reason when dead-lettered.</param>
/// <param name="Description">The dead-letter error description when dead-lettered; the readable cause when retrying.</param>
public sealed record TmsProofOfDeliveryOutcome(ProofOfDeliverySettlement Settlement, string Outcome, string? FailureCode, string? Description, string? ShipmentNumber)
{
    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    public static TmsProofOfDeliveryOutcome Of(int tmsStatusCode, string tmsResponse)
    {
        var body = JsonFields.Parse(tmsResponse);
        if (tmsStatusCode is >= 200 and < 300)
        {
            return new(ProofOfDeliverySettlement.Complete, "Succeeded", null, null, JsonFields.Text(body, "shipmentNumber"));
        }

        if (tmsStatusCode is >= 400 and < 500 and not 408 and not 429)
        {
            var description = JsonFields.Text(body, "detail") ?? JsonFields.Text(body, "title") ?? $"The TMS refused the Proof of delivery with {tmsStatusCode}.";
            return new(ProofOfDeliverySettlement.DeadLetter, "DeadLettered", JsonFields.Text(body, "code") ?? "TmsCallRefused", description, null);
        }

        var cause = tmsStatusCode == 0 ? "The TMS did not answer." : $"The TMS answered {tmsStatusCode}.";
        return new(ProofOfDeliverySettlement.Retry, "Retrying", "TmsUnavailable", cause, null);
    }
}
