using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.CreditNotes.ReportSent;

/// <summary>
/// The Shipper invoice Logic App reports that the Shipper's invoice endpoint acknowledged a Credit note (E1).
/// </summary>
/// <param name="CorrelationId">The correlation ID the ERP created for the Credit note, which the event and the Logic App's report carry.</param>
public sealed record ReportCreditNoteSentCommand(string CreditNoteNumber, string CorrelationId) : ICommand<Result<CreditNoteReportResult>>;

/// <param name="Ignored">True when the Credit note was reported already: the report changed nothing.</param>
public sealed record CreditNoteReportResult(string CreditNoteNumber, string Status, DateTimeOffset? SentAt, bool Ignored = false);
