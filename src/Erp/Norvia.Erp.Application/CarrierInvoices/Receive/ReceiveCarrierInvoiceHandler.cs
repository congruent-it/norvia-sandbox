using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.Receive;

public sealed class ReceiveCarrierInvoiceHandler(
    IRepository<CarrierInvoice> invoices,
    IRepository<CarrierCost> costs,
    IReadRepository<Carrier> carriers,
    IMatchingTolerance tolerance,
    IOutbox outbox,
    TimeProvider time)
    : ICommandHandler<ReceiveCarrierInvoiceCommand, Result<ReceiveCarrierInvoiceResult>>
{
    public async ValueTask<Result<ReceiveCarrierInvoiceResult>> Handle(ReceiveCarrierInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var data = command.Invoice;

        // Idempotent on the Carrier and its invoice number: the same content again is the original, different content is refused.
        if (await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(data.CarrierId, data.InvoiceNumber), cancellationToken) is { } existing)
        {
            return existing.HasSameContentAs(data)
                ? new ReceiveCarrierInvoiceResult(CarrierInvoiceViews.Of(existing), Repeat: true)
                : CarrierInvoiceRefused.Because<ReceiveCarrierInvoiceResult>(
                    CarrierInvoiceRefusal.InvoiceNumberUsed(data.CarrierId.Value, data.InvoiceNumber));
        }

        if (await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(data.CarrierId), cancellationToken) is null)
        {
            return CarrierInvoiceRefused.Because<ReceiveCarrierInvoiceResult>(CarrierInvoiceRefusal.CarrierNotFound(data.CarrierId.Value));
        }

        var references = data.Lines.Select(line => line.CarrierBookingReference).Distinct(StringComparer.Ordinal).ToList();
        var carrierCosts = await costs.ListAsync(new CarrierCostsOfCarrierByReferencesSpec(data.CarrierId, references), cancellationToken);

        var at = time.GetUtcNow();
        var (invoice, refusal) = CarrierInvoice.Receive(data, carrierCosts, tolerance.Amount, at);
        if (invoice is null)
        {
            return CarrierInvoiceRefused.Because<ReceiveCarrierInvoiceResult>(
                refusal ?? throw new InvalidOperationException($"Carrier invoice {data.InvoiceNumber} was neither received nor refused."));
        }

        // One save records the invoice, makes the matched Carrier costs Invoiced and writes the event to the outbox, or none of them.
        if (invoice.Status == CarrierInvoiceStatus.Approved)
        {
            outbox.Add(CarrierInvoiceApprovedV1.Of(invoice, Guid.CreateVersion7(), at));
        }

        await invoices.AddAsync(invoice, cancellationToken);
        return new ReceiveCarrierInvoiceResult(CarrierInvoiceViews.Of(invoice), Repeat: false);
    }
}
