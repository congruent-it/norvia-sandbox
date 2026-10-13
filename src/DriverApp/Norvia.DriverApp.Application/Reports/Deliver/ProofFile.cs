namespace Norvia.DriverApp.Application.Reports.Deliver;

/// <summary>A file the Driver gave the Proof of delivery form: the signature as a PNG, or a photo. What the device said it is, not what its content is.</summary>
public sealed record ProofFile(string ContentType, ReadOnlyMemory<byte> Content);
