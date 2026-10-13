namespace Norvia.Simulator.Domain.Plans;

/// <summary>The business ways a booking can go wrong. Infrastructure faults are not simulated here.</summary>
public enum BadBookingKind
{
    /// <summary>Delivers where the Shipper's Rate Card has no Zone: accepted, then Rating fails with NoMatchingLane.</summary>
    NoLane,

    /// <summary>Sends an earlier booking again: Norvia answers it as already booked.</summary>
    RepeatedReference,

    /// <summary>Reuses an earlier booking's reference for different goods: rejected with ShipperReferenceReused.</summary>
    ConflictingReference,

    /// <summary>Leaves out something Norvia needs, or gives a value it refuses: rejected.</summary>
    InvalidContent,
}
