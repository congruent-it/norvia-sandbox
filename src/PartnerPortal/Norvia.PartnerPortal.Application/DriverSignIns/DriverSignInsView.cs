namespace Norvia.PartnerPortal.Application.DriverSignIns;

/// <summary>The seeded Drivers' sign-in details for the presenter. The password is null where there are no Entra users: locally the Driver app's stand-in lists the Drivers to pick.</summary>
public sealed record DriverSignInsView(string? Password, IReadOnlyList<DriverSignInView> Drivers);
