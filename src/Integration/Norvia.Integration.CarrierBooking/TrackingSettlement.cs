namespace Norvia.Integration.CarrierBooking;

/// <summary>What happens to a tracking queue message once the TMS has answered.</summary>
public enum TrackingSettlement
{
    /// <summary>The TMS recorded the event: the message is completed.</summary>
    Complete,

    /// <summary>The TMS refused the event, which sending it again won't change: the message is dead-lettered.</summary>
    DeadLetter,

    /// <summary>The TMS couldn't be reached: the message is abandoned and comes back, up to the queue's maximum delivery count.</summary>
    Retry,
}
