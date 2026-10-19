using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.CreditNotes.ReportSent;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;
using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Application.CreditNotes.ReportNotDelivered;

public sealed class ReportCreditNoteNotDeliveredHandler(IRepository<CreditNote> creditNotes, IOutbox outbox, TimeProvider time)
    : ICommandHandler<ReportCreditNoteNotDeliveredCommand, Result<CreditNoteReportResult>>
{
    public async ValueTask<Result<CreditNoteReportResult>> Handle(ReportCreditNoteNotDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CreditNoteNumber)
            || await creditNotes.FirstOrDefaultAsync(new CreditNoteToReportSpec(new CreditNoteNumber(command.CreditNoteNumber)), cancellationToken) is not { } note)
        {
            return CreditNoteReports.NotFound<CreditNoteReportResult>(command.CreditNoteNumber);
        }

        var at = time.GetUtcNow();
        if (!note.ReportNotDelivered(command.StatusCode, command.Message, at))
        {
            // Late or repeated: ignored, changing and publishing nothing.
            return new CreditNoteReportResult(note.Id.Value, note.Status.ToString(), note.SentAt, Ignored: true);
        }

        // The status and the event are saved together, or neither is.
        outbox.Add(new ShipperInvoiceNotDeliveredV1(
            Guid.CreateVersion7(),
            at,
            note.CorrelationId,
            ShipperInvoiceNotDeliveredV1.CreditNoteDocumentType,
            note.Id.Value,
            note.ShipperId.Value,
            note.NotDeliveredStatusCode,
            note.NotDeliveredMessage!));
        await creditNotes.SaveChangesAsync(cancellationToken);
        return new CreditNoteReportResult(note.Id.Value, note.Status.ToString(), note.SentAt);
    }
}
