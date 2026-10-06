using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

/// <summary>What became of a Proof of delivery.</summary>
/// <param name="Shipment">The Shipment as the portal has it: a Proof of delivery never changes it.</param>
/// <param name="ProofOfDelivery">The Proof of delivery as it was sent.</param>
/// <param name="Reply">What the Partner API answered.</param>
public sealed record SentProofOfDelivery(CarrierShipmentView Shipment, CarrierProofOfDelivery ProofOfDelivery, TrackingReply Reply);
