namespace Norvia.Integration.CarrierBooking;

/// <summary>One decoded file of a Proof of delivery, for a <c>files</c> part of the TMS call.</summary>
public sealed record MappedProofOfDeliveryFile(string Name, string ContentType, byte[] Content);
