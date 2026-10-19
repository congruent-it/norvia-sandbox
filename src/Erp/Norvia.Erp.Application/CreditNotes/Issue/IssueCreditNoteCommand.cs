using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.CreditNotes.Issue;

/// <summary>
/// Finance issues a Credit note on one line of an issued Shipper invoice, in full or in part, with a reason: the Credit note, the next
/// Credit note number and <c>CreditNoteIssued</c> are saved in one transaction. A refusal carries a stable <c>CreditNoteRefusalCode</c>.
/// </summary>
/// <param name="TransportOrderNumber">Names the invoice line credited.</param>
/// <param name="Amount">The amount without VAT, in EUR.</param>
public sealed record IssueCreditNoteCommand(string InvoiceNumber, string TransportOrderNumber, decimal Amount, string? Reason)
    : ICommand<Result<CreditNoteView>>;
