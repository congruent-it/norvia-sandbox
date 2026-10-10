namespace Norvia.Tms.Domain.Trips;

/// <summary>Whether the Driver app has one version of a Trip, and why not when it hasn't (P11).</summary>
public sealed class TripVersionDelivery
{
    private TripVersionDelivery(int version)
    {
        Version = version;
        State = TripDeliveryState.Sending;
    }

    public int Version { get; private set; }

    public TripDeliveryState State { get; private set; }

    /// <summary>The technical cause the Logic App reported, while the version is Not delivered.</summary>
    public string? Cause { get; private set; }

    /// <summary>When the Logic App's report was recorded; empty while the version is Sending.</summary>
    public DateTimeOffset? ReportedAt { get; private set; }

    /// <summary>A version that has just been published.</summary>
    public static TripVersionDelivery Start(int version) => new(version);

    internal void Deliver(DateTimeOffset at)
    {
        State = TripDeliveryState.Delivered;
        Cause = null;
        ReportedAt = at;
    }

    internal void Fail(string cause, DateTimeOffset at)
    {
        State = TripDeliveryState.NotDelivered;
        Cause = cause;
        ReportedAt = at;
    }

    internal void SendAgain()
    {
        State = TripDeliveryState.Sending;
        Cause = null;
        ReportedAt = null;
    }
}
