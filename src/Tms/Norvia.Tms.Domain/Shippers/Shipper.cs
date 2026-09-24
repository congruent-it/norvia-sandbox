namespace Norvia.Tms.Domain.Shippers;

/// <summary>
/// The TMS's read copy of a Shipper. The ERP owns Shipper master data (ADR 0002); until its sync exists the copy comes from the seed file.
/// </summary>
public sealed record Shipper(ShipperId Id, string Name, bool Active);
