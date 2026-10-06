namespace Norvia.Integration.CarrierBooking;

/// <summary>What happens to a Proof of delivery queue message once the TMS has answered.</summary>
public enum ProofOfDeliverySettlement
{
    /// <summary>The TMS recorded the Proof of delivery: the blob is deleted and the message completed.</summary>
    Complete,

    /// <summary>The TMS refused the Proof of delivery, which sending it again won't change: the message is dead-lettered and the blob kept.</summary>
    DeadLetter,

    /// <summary>The TMS couldn't be reached: the message is abandoned and comes back, up to the queue's maximum delivery count, and the blob is kept.</summary>
    Retry,
}
