using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>What the Send Proof of delivery form collects: the Carrier's side of it, before the portal adds the rest.</summary>
public sealed record ProofOfDeliveryDraft(string ReceivedBy, ProofOfDeliveryCondition Condition, string? Remark, IReadOnlyList<ProofOfDeliveryUpload> Files);
