namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>Who receives the goods at a saved address.</summary>
public sealed record SavedConsignee(string Name, string? ContactName, string? Phone);
