using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.ListMissing;

/// <summary>A Delivered Shipment in the Planner's list of Shipments without a current Proof of delivery (P6).</summary>
/// <param name="CarriedBy">Who carries it, so the Carrier for a Carrier Shipment.</param>
/// <param name="DeliveredAt">The event time of its Delivered Tracking event.</param>
/// <param name="IsProofOfDeliveryMissing">
/// Whether the TMS's missing threshold has passed since it was delivered. Only a flag for the Planner, which publishes
/// nothing; flagged Shipments are listed first, then the oldest delivered.
/// </param>
public sealed record DeliveredShipmentWithoutProofOfDeliverySummary(
    string ShipmentNumber,
    CarriedByView CarriedBy,
    DateTimeOffset DeliveredAt,
    bool IsProofOfDeliveryMissing);
