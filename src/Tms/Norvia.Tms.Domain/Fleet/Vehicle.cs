namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// The TMS's read copy of a Vehicle. The ERP owns Vehicle master data (ADR 0014) and replicates each change (E3); until a change arrives
/// the copy is the seed file's, at Master-data revision 1. The TMS never edits it. What the TMS does own, such as when a Vehicle is off the road,
/// is kept apart from it.
/// </summary>
/// <param name="Revision">The Master-data revision of the ERP record this copy was made from.</param>
public sealed record Vehicle(VehicleId Id, string Registration, VehicleType Type, int PayloadKg, int PalletPlaces, bool Active, int Revision = 1);
