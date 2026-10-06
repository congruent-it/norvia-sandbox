namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>One file backing a Proof of delivery: a signed CMR, a signature or a photo.</summary>
public sealed record CarrierProofOfDeliveryFile(string Name, string ContentType, byte[] Content);
