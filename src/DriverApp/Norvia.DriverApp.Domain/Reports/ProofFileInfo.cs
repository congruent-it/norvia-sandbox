namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Domain needs to know of a file of a Proof of delivery: what the Driver's device said it is, and how big it is.</summary>
public sealed record ProofFileInfo(string ContentType, long SizeBytes);
