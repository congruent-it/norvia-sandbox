namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>A file of a Proof of delivery as the event names it: the claim check of ADR 0009.</summary>
internal sealed record ProofFileReference(string Name, string ContentType, string BlobReference);
