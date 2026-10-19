using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

/// <summary>
/// Finance accepts a Carrier invoice line To decide: at the invoiced amount, or at <paramref name="AdjustedAmount"/> with a remark. Its
/// Carrier cost becomes Invoiced. When it is the last line to decide the invoice becomes Approved and <c>CarrierInvoiceApproved</c> is
/// published through the outbox in the same transaction; otherwise nothing is published.
/// </summary>
public sealed record AcceptCarrierInvoiceLineCommand(
    CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference, decimal? AdjustedAmount, string? Remark)
    : ICommand<Result<CarrierInvoiceView>>;
