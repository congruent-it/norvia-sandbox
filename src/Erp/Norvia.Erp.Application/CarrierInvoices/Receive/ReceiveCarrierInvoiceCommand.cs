using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices.Receive;

/// <summary>
/// Records a Carrier invoice and matches every line against its Carrier cost, in one transaction. An invoice whose lines all match is
/// Approved, and <c>CarrierInvoiceApproved</c> is published through the outbox in that same transaction.
/// </summary>
public sealed record ReceiveCarrierInvoiceCommand(CarrierInvoiceData Invoice) : ICommand<Result<ReceiveCarrierInvoiceResult>>;

/// <param name="Invoice">The recorded Carrier invoice, or the original one for a repeat.</param>
/// <param name="Repeat">True when the Carrier had already sent this invoice number with this content.</param>
public sealed record ReceiveCarrierInvoiceResult(CarrierInvoiceView Invoice, bool Repeat);
