namespace Norvia.PartnerPortal.Application.ShipperTracking;

public sealed record ShipperProofFile(Guid FileId, string Name, string ContentType, long SizeBytes);
