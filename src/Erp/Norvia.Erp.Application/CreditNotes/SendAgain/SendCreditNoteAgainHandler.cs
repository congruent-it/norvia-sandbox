using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.CreditNotes.SendAgain;

public sealed partial class SendCreditNoteAgainHandler(
    IRepository<CreditNote> creditNotes,
    IReadRepository<Shipper> shippers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SendCreditNoteAgainHandler> logger)
    : ICommandHandler<SendCreditNoteAgainCommand, Result<CreditNoteView>>
{
    public async ValueTask<Result<CreditNoteView>> Handle(SendCreditNoteAgainCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CreditNoteNumber)
            || await creditNotes.FirstOrDefaultAsync(new CreditNoteToReportSpec(new CreditNoteNumber(command.CreditNoteNumber)), cancellationToken) is not { } note)
        {
            return Refused(CreditNoteRefusalCode.CreditNoteNotFound, $"The ERP has no Credit note {command.CreditNoteNumber}.");
        }

        // The Credit note's own trail continues: it is the same Credit note, sent again.
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = note.CorrelationId,
            [TrackingKeys.Flow] = Flows.Invoicing,
            [TrackingKeys.PartyId] = note.ShipperId.Value,
            [TrackingKeys.DocumentNumber] = note.Id.Value,
        });

        if (!note.SendAgain())
        {
            LogRefused(logger, note.Id.Value, note.Status.ToString());
            return Refused(
                CreditNoteRefusalCode.CreditNoteNotSendableAgain,
                $"Credit note {note.Id.Value} is {note.Status}; only a Not delivered Credit note can be sent again.");
        }

        var shipper = await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(note.ShipperId), cancellationToken);
        var view = CreditNoteViews.Of(note, shipper?.Name ?? note.ShipperId.Value);

        outbox.Add(new CreditNoteIssuedV1(Guid.CreateVersion7(), time.GetUtcNow(), note.CorrelationId, view));
        await creditNotes.SaveChangesAsync(cancellationToken);

        LogSentAgain(logger, note.Id.Value);
        return view;
    }

    private static Result<CreditNoteView> Refused(CreditNoteRefusalCode code, string message) =>
        Result<CreditNoteView>.Invalid(new ValidationError("CreditNote", message, code.ToString(), ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Credit note {CreditNoteNumber} sent again")]
    private static partial void LogSentAgain(ILogger logger, string creditNoteNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Send again refused: Credit note {CreditNoteNumber} is {Status}")]
    private static partial void LogRefused(ILogger logger, string creditNoteNumber, string status);
}
