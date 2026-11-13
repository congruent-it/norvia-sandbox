namespace Norvia.Tms.Domain.Shippers;

/// <summary>
/// The TMS's read copy of a Shipper. The ERP owns Shipper master data (ADR 0002) and replicates each change (E3); until a change arrives
/// the copy is the seed file's, at Master-data revision 1.
/// </summary>
/// <param name="Revision">The Master-data revision of the ERP record this copy was made from.</param>
public sealed record Shipper(ShipperId Id, string Name, bool Active, int Revision = 1);
