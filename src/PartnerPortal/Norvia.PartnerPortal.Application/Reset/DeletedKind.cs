namespace Norvia.PartnerPortal.Application.Reset;

/// <summary>How many records of one kind a reset deleted.</summary>
public sealed record DeletedKind(string Kind, int Count);
