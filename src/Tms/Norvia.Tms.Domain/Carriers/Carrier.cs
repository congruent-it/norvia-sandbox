namespace Norvia.Tms.Domain.Carriers;

/// <summary>
/// The TMS's read copy of a Carrier. The ERP owns Carrier master data (ADR 0002) and replicates each change (E3); until a change arrives
/// the copy is the seed file's, at Master-data revision 1. The TMS never edits it.
/// </summary>
/// <param name="Revision">The Master-data revision of the ERP record this copy was made from.</param>
public sealed record Carrier(CarrierId Id, string Name, bool Active, int Revision = 1);
