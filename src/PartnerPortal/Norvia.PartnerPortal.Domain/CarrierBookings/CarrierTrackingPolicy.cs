namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>How a Carrier Persona sends Tracking events: the mode, and the delay before each automatic one.</summary>
public sealed record CarrierTrackingPolicy
{
    public static readonly TimeSpan LongestDelay = TimeSpan.FromMinutes(2);

    public CarrierTrackingPolicy(TrackingMode mode, TimeSpan delay)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a tracking mode.");
        }

        if (delay < TimeSpan.Zero || delay > LongestDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, $"The delay is between none and {LongestDelay.TotalSeconds:0} seconds.");
        }

        Mode = mode;
        Delay = delay;
    }

    public TrackingMode Mode { get; }

    public TimeSpan Delay { get; }

    /// <summary>The delay before each automatic Tracking event of a Shipment confirmed under this policy; null for <see cref="TrackingMode.Manual"/>.</summary>
    public TimeSpan? AutoProgressDelay => Mode == TrackingMode.AutoProgress ? Delay : null;
}
