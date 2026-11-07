using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

/// <summary>
/// Finance rejects a Carrier invoice line To decide, with a reason. Its Carrier cost, if any, stays as it was. When it is the last line
/// to decide the invoice becomes Approved and <c>CarrierInvoiceApproved</c> is published through the outbox in the same transaction.
/// </summary>
/// <param name="ActingOn">The Agent run whose Suggestion Finance acted on, which the trace then names. Null when Finance acted on their own.</param>
public sealed record RejectCarrierInvoiceLineCommand(CarrierId CarrierId, string InvoiceNumber, string CarrierBookingReference, string? Reason, AgentRun? ActingOn = null)
    : ICommand<Result<CarrierInvoiceView>>;
