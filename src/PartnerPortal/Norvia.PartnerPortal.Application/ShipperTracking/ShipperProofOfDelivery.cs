using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>The current Proof of delivery. <paramref name="DeliveredAt"/> is local to the delivery address, <c>dd-MM-yyyy HH:mm</c>, as returned.</summary>
public sealed record ShipperProofOfDelivery(string ReceivedBy, string DeliveredAt, ProofOfDeliveryCondition Condition, IReadOnlyList<ShipperProofFile> Files);
