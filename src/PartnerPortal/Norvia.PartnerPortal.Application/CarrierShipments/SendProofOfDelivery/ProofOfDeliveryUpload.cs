namespace Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

/// <summary>A file the Carrier attaches to a Proof of delivery, as it was uploaded or bundled.</summary>
public sealed record ProofOfDeliveryUpload(string Name, string ContentType, byte[] Content);
