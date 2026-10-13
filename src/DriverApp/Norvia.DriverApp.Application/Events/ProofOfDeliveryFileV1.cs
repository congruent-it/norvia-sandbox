namespace Norvia.DriverApp.Application.Events;

/// <summary>A file of a Proof of delivery as the event carries it: not the file, but where the Driver app put it (the claim check of ADR 0009).</summary>
/// <param name="BlobReference">The blob's name in the Driver app's transit container.</param>
public sealed record ProofOfDeliveryFileV1(string Name, string ContentType, string BlobReference);
