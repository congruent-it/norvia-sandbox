using Mediator;

namespace Norvia.Erp.Application.ShipperInvoices.Open;

/// <summary>One issued Shipper invoice with its lines, or null when the ERP has none with that number.</summary>
public sealed record OpenShipperInvoiceQuery(string InvoiceNumber) : IQuery<ShipperInvoiceView?>;
