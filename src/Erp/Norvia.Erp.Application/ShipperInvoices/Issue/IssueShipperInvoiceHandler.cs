using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.ShipperInvoices.Issue;

/// <summary>
/// The Invoice run for one Shipper. The Shipper's Waiting lines, the invoice, the next invoice number and the event are all saved by one
/// save, so a rolled-back run takes no number and publishes nothing. Two runs that overlap, or a request sent twice, conflict on the lines'
/// row version: the one that loses is run again by the caller, finds no Waiting line and issues nothing.
/// </summary>
public sealed partial class IssueShipperInvoiceHandler(
    IRepository<BillableLine> lines,
    IRepository<InvoiceNumberSequence> sequences,
    IRepository<ShipperInvoice> invoices,
    IReadRepository<Shipper> shippers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<IssueShipperInvoiceHandler> logger)
    : ICommandHandler<IssueShipperInvoiceCommand, Result<IssueShipperInvoiceResult>>
{
    public async ValueTask<Result<IssueShipperInvoiceResult>> Handle(IssueShipperInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var shipperId = new ShipperId(command.ShipperId);

        if (await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(shipperId), cancellationToken) is not { } shipper)
        {
            return Result.NotFound($"The ERP has no Shipper {command.ShipperId}.");
        }

        var waiting = await lines.ListAsync(new WaitingBillableLinesToInvoiceSpec(shipperId), cancellationToken);
        if (waiting.Count == 0)
        {
            LogNothingToInvoice(logger, command.ShipperId);
            return new IssueShipperInvoiceResult(null);
        }

        var runAt = time.GetUtcNow();
        var year = ShipperInvoice.InvoiceYear(runAt);
        var sequence = await sequences.FirstOrDefaultAsync(new InvoiceNumberSequenceOfYearSpec(year), cancellationToken);
        if (sequence is null)
        {
            // A year's first run starts the sequence in a save of its own, which takes no number. Two first runs conflict on its key.
            sequence = new InvoiceNumberSequence(year);
            await sequences.AddAsync(sequence, cancellationToken);
        }

        // A new trail for this invoice: the intake and billing correlation IDs stay on the lines.
        var correlationId = Guid.NewGuid().ToString();
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.Invoicing,
            [TrackingKeys.PartyId] = command.ShipperId,
        });

        var invoice = ShipperInvoice.Issue(sequence.TakeNext(), shipperId, waiting, runAt, correlationId);
        var view = ShipperInvoiceViews.Of(invoice, shipper.Name);

        // The lines and the sequence are tracked and changed; this one save writes them with the invoice and the outbox message, or none.
        outbox.Add(new ShipperInvoiceIssuedV1(Guid.CreateVersion7(), runAt, correlationId, view));
        await invoices.AddAsync(invoice, cancellationToken);

        LogIssued(logger, view.InvoiceNumber, command.ShipperId, waiting.Count);
        return new IssueShipperInvoiceResult(view);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper {ShipperId} has no Waiting billable lines; no invoice issued")]
    private static partial void LogNothingToInvoice(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued Shipper invoice {InvoiceNumber} to {ShipperId} over {LineCount} billable lines")]
    private static partial void LogIssued(ILogger logger, string invoiceNumber, string shipperId, int lineCount);
}
