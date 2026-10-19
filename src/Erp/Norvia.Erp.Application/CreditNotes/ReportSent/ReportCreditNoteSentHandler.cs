using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Application.CreditNotes.ReportSent;

public sealed class ReportCreditNoteSentHandler(IRepository<CreditNote> creditNotes, TimeProvider time)
    : ICommandHandler<ReportCreditNoteSentCommand, Result<CreditNoteReportResult>>
{
    public async ValueTask<Result<CreditNoteReportResult>> Handle(ReportCreditNoteSentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CreditNoteNumber)
            || await creditNotes.FirstOrDefaultAsync(new CreditNoteToReportSpec(new CreditNoteNumber(command.CreditNoteNumber)), cancellationToken) is not { } note)
        {
            return CreditNoteReports.NotFound<CreditNoteReportResult>(command.CreditNoteNumber);
        }

        if (!note.ReportSent(time.GetUtcNow()))
        {
            // Reported already: ignored, changing nothing.
            return new CreditNoteReportResult(note.Id.Value, note.Status.ToString(), note.SentAt, Ignored: true);
        }

        await creditNotes.SaveChangesAsync(cancellationToken);
        return new CreditNoteReportResult(note.Id.Value, note.Status.ToString(), note.SentAt);
    }
}
