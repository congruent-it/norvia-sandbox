namespace Norvia.PartnerPortal.Infrastructure.DriverSignIns;

/// <summary>
/// The Entra test users the estate made for the seeded Drivers. Without a domain there are none, as when the apps run locally,
/// where the Driver app's stand-in signs in as any seeded Driver.
/// </summary>
public sealed class DriverSignInOptions
{
    public const string SectionName = "DriverSignIns";

    /// <summary>The domain of the test users' user names, such as <c>norvia.onmicrosoft.com</c>.</summary>
    public string? Domain { get; init; }

    /// <summary>The password the estate set on every test user.</summary>
    public string? Password { get; init; }
}
