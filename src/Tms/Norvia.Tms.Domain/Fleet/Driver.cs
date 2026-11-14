namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// The TMS's read copy of a Driver. The ERP owns Driver master data (ADR 0014) and replicates each change (E3); until a change arrives
/// the copy is the seed file's, at Master-data revision 1. The TMS never edits it.
/// </summary>
/// <param name="Revision">The Master-data revision of the ERP record this copy was made from.</param>
public sealed record Driver(DriverId Id, string Name, IReadOnlySet<LicenceCategory> Licences, bool Active, int Revision = 1)
{
    /// <summary>True when the Driver's highest licence covers what the Vehicle type needs; a Driver without a licence drives nothing.</summary>
    public bool CanDrive(VehicleType type) => Licences.Count > 0 && LicenceRules.Covers(Licences.Max(), LicenceRules.Needed(type));
}
