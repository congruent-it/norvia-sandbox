using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>How the flow's run ended, and so what happens to the queue message: the Logic App's settling steps.</summary>
/// <param name="DeadLetterReason">The dead-letter reason when <see cref="ProofOfDeliverySettlement.DeadLetter"/>.</param>
/// <param name="DeadLetterDescription">The dead-letter error description when <see cref="ProofOfDeliverySettlement.DeadLetter"/>.</param>
internal sealed record ProofOfDeliveryRun(ProofOfDeliverySettlement Settlement, string? DeadLetterReason = null, string? DeadLetterDescription = null)
{
    /// <summary>True when the workflow's run would end Failed: the Proof of delivery wasn't recorded.</summary>
    public bool RunFailed => Settlement != ProofOfDeliverySettlement.Complete;
}
