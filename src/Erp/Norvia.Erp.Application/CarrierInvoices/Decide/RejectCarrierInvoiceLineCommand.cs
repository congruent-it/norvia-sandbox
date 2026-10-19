using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

/// <summary>
/// Finance rejects a Carrier invoice line To decide, with a reason. Its Carrier cost, if any, stays as it was. When it is the last line
/// to decide the invoice becomes Approved and <c>CarrierInvoiceApproved</c> is published through the outbox in the same transaction.
/// </summary>
public sealed record RejectCarrierInvoiceLineCommand(CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference, string? Reason)
    : ICommand<Result<CarrierInvoiceView>>;
