namespace Norvia.Simulator.Domain.Carriers;

/// <summary>What is true of a tracking event wherever it is reported: where it happens and what the Carrier says about it.</summary>
public static class CarrierEventRules
{
    /// <summary>Whether the event happens at the collection address; the rest happen at the delivery address.</summary>
    public static bool HappensAtPickup(this CarrierEvent carrierEvent) =>
        carrierEvent is CarrierEvent.Collected or CarrierEvent.Departed or CarrierEvent.CollectionFailed or CarrierEvent.Damaged;

    /// <summary>The remark a Carrier gives with a failure or damage; none for progress.</summary>
    public static string? Remark(this CarrierEvent carrierEvent) => carrierEvent switch
    {
        CarrierEvent.CollectionFailed => "Nobody at the collection address",
        CarrierEvent.DeliveryFailed => "Nobody at the delivery address",
        CarrierEvent.Damaged => "A pallet was damaged on the way",
        _ => null,
    };
}
