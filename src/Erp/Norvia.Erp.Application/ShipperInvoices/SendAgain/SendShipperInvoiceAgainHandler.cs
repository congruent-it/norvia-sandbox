using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.ShipperInvoices.SendAgain;

public sealed partial class SendShipperInvoiceAgainHandler(
    IRepository<ShipperInvoice> invoices,
    IReadRepository<Shipper> shippers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SendShipperInvoiceAgainHandler> logger)
    : ICommandHandler<SendShipperInvoiceAgainCommand, Result<ShipperInvoiceView>>
{
    public async ValueTask<Result<ShipperInvoiceView>> Handle(SendShipperInvoiceAgainCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.InvoiceNumber)
            || await invoices.FirstOrDefaultAsync(new ShipperInvoiceToReportSpec(new InvoiceNumber(command.InvoiceNumber)), cancellationToken) is not { } invoice)
        {
            return Refused(ShipperInvoiceRefusalCode.ShipperInvoiceNotFound, $"The ERP has no Shipper invoice {command.InvoiceNumber}.");
        }

        // The invoice's own trail continues: it is the same invoice, sent again.
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = invoice.CorrelationId,
            [TrackingKeys.Flow] = Flows.Invoicing,
            [TrackingKeys.PartyId] = invoice.ShipperId.Value,
            [TrackingKeys.DocumentNumber] = invoice.Id.Value,
        });

        if (!invoice.SendAgain())
        {
            LogRefused(logger, invoice.Id.Value, invoice.Status.ToString());
            return Refused(
                ShipperInvoiceRefusalCode.ShipperInvoiceNotSendableAgain,
                $"Shipper invoice {invoice.Id.Value} is {invoice.Status}; only a Not delivered invoice can be sent again.");
        }

        var shipper = await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(invoice.ShipperId), cancellationToken);
        var view = ShipperInvoiceViews.Of(invoice, shipper?.Name ?? invoice.ShipperId.Value);

        outbox.Add(new ShipperInvoiceIssuedV1(Guid.CreateVersion7(), time.GetUtcNow(), invoice.CorrelationId, view));
        await invoices.SaveChangesAsync(cancellationToken);

        LogSentAgain(logger, invoice.Id.Value);
        return view;
    }

    private static Result<ShipperInvoiceView> Refused(ShipperInvoiceRefusalCode code, string message) =>
        Result<ShipperInvoiceView>.Invalid(new ValidationError("ShipperInvoice", message, code.ToString(), ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper invoice {InvoiceNumber} sent again")]
    private static partial void LogSentAgain(ILogger logger, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Send again refused: Shipper invoice {InvoiceNumber} is {Status}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string status);
}
