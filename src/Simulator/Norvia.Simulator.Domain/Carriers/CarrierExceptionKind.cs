namespace Norvia.Simulator.Domain.Carriers;

/// <summary>What goes wrong on the way, and so opens an Exception (P7): the failure mix of the Carrier stage.</summary>
public enum CarrierExceptionKind
{
    CollectionFailed,
    DeliveryFailed,
    Damaged,

    /// <summary>The Proof of delivery comes with reservations.</summary>
    Reservations,
}
