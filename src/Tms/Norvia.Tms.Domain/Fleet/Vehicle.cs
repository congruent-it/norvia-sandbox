namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// The TMS's read copy of a Vehicle. The ERP owns Vehicle master data (ADR 0014); until its sync exists the copy comes
/// from the seed file, and the TMS never edits it. What the TMS does own, such as when a Vehicle is off the road, is kept apart from it.
/// </summary>
public sealed record Vehicle(VehicleId Id, string Registration, VehicleType Type, int PayloadKg, int PalletPlaces, bool Active);
