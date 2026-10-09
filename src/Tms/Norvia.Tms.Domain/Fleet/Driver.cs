namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// The TMS's read copy of a Driver. The ERP owns Driver master data (ADR 0014); until its sync exists the copy comes
/// from the seed file, and the TMS never edits it.
/// </summary>
public sealed record Driver(DriverId Id, string Name, IReadOnlySet<LicenceCategory> Licences, bool Active)
{
    /// <summary>True when the Driver's highest licence covers what the Vehicle type needs; a Driver without a licence drives nothing.</summary>
    public bool CanDrive(VehicleType type) => Licences.Count > 0 && LicenceRules.Covers(Licences.Max(), LicenceRules.Needed(type));
}
