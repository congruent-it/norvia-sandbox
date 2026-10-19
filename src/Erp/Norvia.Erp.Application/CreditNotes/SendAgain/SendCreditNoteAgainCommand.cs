using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.CreditNotes.SendAgain;

/// <summary>
/// Finance chose <b>Send again</b> for a Not delivered Credit note: it is Issued once more and <c>CreditNoteIssued</c> is published again
/// with the same Credit note, in one transaction. Nothing else about the Credit note changes.
/// </summary>
public sealed record SendCreditNoteAgainCommand(string CreditNoteNumber) : ICommand<Result<CreditNoteView>>;
