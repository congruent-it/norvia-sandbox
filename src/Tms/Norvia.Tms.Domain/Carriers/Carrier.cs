namespace Norvia.Tms.Domain.Carriers;

/// <summary>
/// The TMS's read copy of a Carrier. The ERP owns Carrier master data (ADR 0002); until its sync exists the copy comes
/// from the seed file, and the TMS never edits it.
/// </summary>
public sealed record Carrier(CarrierId Id, string Name, bool Active);
