namespace Norvia.PartnerPortal.Application.DriverSignIns;

/// <summary>The sign-in details of the seeded Drivers, whom the Driver app signs in with Entra ID (P12).</summary>
public interface IDriverSignInDirectory
{
    DriverSignInsView Current { get; }
}
