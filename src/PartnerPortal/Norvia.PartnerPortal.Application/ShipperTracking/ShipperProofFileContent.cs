namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>A Proof of delivery file as Norvia streamed it.</summary>
public sealed record ShipperProofFileContent(string Name, string ContentType, byte[] Content);
