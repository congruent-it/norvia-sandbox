using Ardalis.SharedKernel;

namespace Norvia.DriverApp.Domain.Trips;

/// <summary>
/// A Trip as the Driver app holds it: only the latest version the Trip dispatch Logic App sent, keyed by Trip number. A
/// version lower than or equal to the one held is ignored, so a retry, a duplicate or a version that arrives late
/// changes nothing (ADR 0015).
/// </summary>
public sealed class DriverTrip : EntityBase<TripNumber>, IAggregateRoot
{
    private DriverTrip()
    {
        DriverId = string.Empty;
        Details = null!;
    }

    public DriverTrip(string tripNumber, int version, string driverId, TripDetails details)
        : this()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tripNumber);
        Id = new TripNumber(tripNumber);
        Apply(version, driverId, details);
    }

    public int Version { get; private set; }

    /// <summary>The Driver the latest version is for, the only Driver who sees the Trip.</summary>
    public string DriverId { get; private set; }

    public TripDetails Details { get; private set; }

    /// <summary>Replaces the held version with a higher one; returns false, and changes nothing, for an equal or lower one.</summary>
    public bool ReplaceWith(int version, string driverId, TripDetails details)
    {
        if (version <= Version)
        {
            return false;
        }

        Apply(version, driverId, details);
        return true;
    }

    private void Apply(int version, string driverId, TripDetails details)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        ArgumentNullException.ThrowIfNull(details);
        Version = version;
        DriverId = driverId;
        Details = details;
    }
}
