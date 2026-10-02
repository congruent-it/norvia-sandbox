namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// How a Carrier Persona answers its booking requests: the mode, the reason it rejects with when the mode is
/// <see cref="CarrierAnswerMode.AutoReject"/>, and the delay before an automatic answer.
/// </summary>
public sealed record CarrierAnswerPolicy
{
    public static readonly TimeSpan LongestDelay = TimeSpan.FromMinutes(2);

    public CarrierAnswerPolicy(CarrierAnswerMode mode, CarrierRejectionReason rejectionReason, TimeSpan delay)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not an answer mode.");
        }

        if (!Enum.IsDefined(rejectionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason), rejectionReason, "Not a rejection reason.");
        }

        if (delay < TimeSpan.Zero || delay > LongestDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, $"The delay is between none and {LongestDelay.TotalSeconds:0} seconds.");
        }

        Mode = mode;
        RejectionReason = rejectionReason;
        Delay = delay;
    }

    public CarrierAnswerMode Mode { get; }

    public CarrierRejectionReason RejectionReason { get; }

    public TimeSpan Delay { get; }

    /// <summary>
    /// What a request received at <paramref name="receivedAt"/> is answered with automatically, and when; null for
    /// <see cref="CarrierAnswerMode.Manual"/>. A request keeps it: changing the policy later doesn't touch a request in flight.
    /// </summary>
    public ScheduledAnswer? ScheduleFor(DateTimeOffset receivedAt) => Mode switch
    {
        CarrierAnswerMode.Manual => null,
        _ => new ScheduledAnswer(Mode, RejectionReason, receivedAt + Delay),
    };
}
