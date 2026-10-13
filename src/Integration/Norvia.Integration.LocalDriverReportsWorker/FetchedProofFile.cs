namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>A file fetched from the transit container, ready to be a part of the TMS's multipart call.</summary>
internal sealed record FetchedProofFile(string Name, string ContentType, byte[] Content);
