using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.ShipperInvoices.SendAgain;

/// <summary>
/// Finance chose <b>Send again</b> for a Not delivered Shipper invoice: the invoice is Issued once more and <c>ShipperInvoiceIssued</c> is
/// published again with the same invoice, in one transaction. Nothing else about the invoice changes.
/// </summary>
public sealed record SendShipperInvoiceAgainCommand(string InvoiceNumber) : ICommand<Result<ShipperInvoiceView>>;
