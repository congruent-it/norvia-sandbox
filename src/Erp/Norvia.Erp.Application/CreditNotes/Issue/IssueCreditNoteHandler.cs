using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.CreditNotes.Issue;

/// <summary>
/// Issues a Credit note. The Credit note, the next number of its own sequence and the event are saved by one save, so a rolled-back issue
/// takes no number and publishes nothing. Two Credit notes issued at once conflict on the sequence's row version: the one that loses is run
/// again by the caller and checks its amount against the Credit notes that exist by then. The invoice is only read.
/// </summary>
public sealed partial class IssueCreditNoteHandler(
    IReadRepository<ShipperInvoice> invoices,
    IRepository<CreditNote> creditNotes,
    IRepository<CreditNoteNumberSequence> sequences,
    IReadRepository<Shipper> shippers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<IssueCreditNoteHandler> logger)
    : ICommandHandler<IssueCreditNoteCommand, Result<CreditNoteView>>
{
    public async ValueTask<Result<CreditNoteView>> Handle(IssueCreditNoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.InvoiceNumber)
            || await invoices.FirstOrDefaultAsync(new ShipperInvoiceByNumberSpec(new InvoiceNumber(command.InvoiceNumber)), cancellationToken) is not { } invoice)
        {
            return Refused(CreditNoteRefusalCode.ShipperInvoiceNotFound, $"The ERP has no Shipper invoice {command.InvoiceNumber}.");
        }

        var earlier = await creditNotes.ListAsync(new CreditNotesOfInvoiceLineSpec(invoice.Id, command.TransportOrderNumber), cancellationToken);
        var alreadyCredited = earlier.Sum(note => note.AmountWithoutVat);
        if (CreditNote.Refuse(invoice, command.TransportOrderNumber, command.Amount, command.Reason, alreadyCredited) is { } refusal)
        {
            LogRefused(logger, invoice.Id.Value, command.TransportOrderNumber, refusal.Code.ToString());
            return Refused(refusal.Code, refusal.Message);
        }

        var issuedAt = time.GetUtcNow();
        var year = ShipperInvoice.InvoiceYear(issuedAt);
        var sequence = await sequences.FirstOrDefaultAsync(new CreditNoteNumberSequenceOfYearSpec(year), cancellationToken);
        if (sequence is null)
        {
            // A year's first Credit note starts the sequence in a save of its own, which takes no number. Two first ones conflict on its key.
            sequence = new CreditNoteNumberSequence(year);
            await sequences.AddAsync(sequence, cancellationToken);
        }

        // A new trail for this Credit note: the invoice's own trail is left as it was.
        var correlationId = Guid.NewGuid().ToString();
        var note = CreditNote.Issue(sequence.TakeNext(), invoice, command.TransportOrderNumber, command.Amount, command.Reason!, alreadyCredited, issuedAt, correlationId);
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.Invoicing,
            [TrackingKeys.PartyId] = invoice.ShipperId.Value,
            [TrackingKeys.DocumentNumber] = note.Id.Value,
        });

        var shipper = await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(invoice.ShipperId), cancellationToken);
        var view = CreditNoteViews.Of(note, shipper?.Name ?? invoice.ShipperId.Value);

        // The sequence is tracked and changed; this one save writes it with the Credit note and the outbox message, or none.
        outbox.Add(new CreditNoteIssuedV1(Guid.CreateVersion7(), issuedAt, correlationId, view));
        await creditNotes.AddAsync(note, cancellationToken);

        LogIssued(logger, view.CreditNoteNumber, view.InvoiceNumber, view.TransportOrderNumber);
        return view;
    }

    private static Result<CreditNoteView> Refused(CreditNoteRefusalCode code, string message) =>
        Result<CreditNoteView>.Invalid(new ValidationError("CreditNote", message, code.ToString(), ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credit note on {InvoiceNumber} line {TransportOrderNumber} refused: {Code}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string transportOrderNumber, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued Credit note {CreditNoteNumber} on Shipper invoice {InvoiceNumber} line {TransportOrderNumber}")]
    private static partial void LogIssued(ILogger logger, string creditNoteNumber, string invoiceNumber, string transportOrderNumber);
}
