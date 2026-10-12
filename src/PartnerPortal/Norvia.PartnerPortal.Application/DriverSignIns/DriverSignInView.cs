namespace Norvia.PartnerPortal.Application.DriverSignIns;

/// <summary>A seeded Driver and the Entra test user who signs in as them to the Driver app. The user name is null where there are no Entra users, locally.</summary>
public sealed record DriverSignInView(string DriverId, string Name, string? UserName);
