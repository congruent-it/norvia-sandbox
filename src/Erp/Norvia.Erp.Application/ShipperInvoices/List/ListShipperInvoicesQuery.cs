using Mediator;

namespace Norvia.Erp.Application.ShipperInvoices.List;

/// <summary>The issued Shipper invoices, the newest first.</summary>
public sealed record ListShipperInvoicesQuery : IQuery<IReadOnlyList<ShipperInvoiceView>>;
