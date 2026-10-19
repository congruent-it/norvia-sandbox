using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.ShipperInvoices.Issue;

/// <summary>
/// Finance chose <b>Invoice now</b> for a Shipper: issues one Shipper invoice over all its Waiting lines and publishes
/// <c>ShipperInvoiceIssued</c>, in one transaction. A Shipper without Waiting lines gets no invoice.
/// </summary>
public sealed record IssueShipperInvoiceCommand(string ShipperId) : ICommand<Result<IssueShipperInvoiceResult>>;

/// <param name="Invoice">The invoice issued; null when the Shipper had no Waiting lines.</param>
public sealed record IssueShipperInvoiceResult(ShipperInvoiceView? Invoice);
