using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierCosts.Record;

/// <summary>
/// Records a Carrier cost and matches again, in the same save, the Unknown booking lines of that Carrier waiting for it. The invoices are
/// changed first and adding the Carrier cost saves everything, so the Carrier cost, the lines, the Invoiced status and any
/// <c>CarrierInvoiceApproved</c> in the outbox commit together or not at all.
/// </summary>
public sealed class RecordCarrierCostHandler(
    IRepository<CarrierCost> costs,
    IRepository<CarrierInvoice> invoices,
    IReadRepository<Carrier> carriers,
    IMatchingTolerance tolerance,
    IOutbox outbox,
    TimeProvider time)
    : ICommandHandler<RecordCarrierCostCommand, Result<RecordCarrierCostResult>>
{
    public async ValueTask<Result<RecordCarrierCostResult>> Handle(RecordCarrierCostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var data = command.Cost;

        // Idempotent on the Carrier booking number: the same content again is the original Carrier cost, different content is a conflict.
        if (await costs.FirstOrDefaultAsync(new CarrierCostByBookingNumberSpec(data.CarrierBookingNumber), cancellationToken) is { } existing)
        {
            return existing.HasSameContentAs(data)
                ? new RecordCarrierCostResult(CarrierCostViews.Of(existing), Repeat: true)
                : CarrierCostRefused.Because<RecordCarrierCostResult>(CarrierCostRefusal.Conflict(data.CarrierBookingNumber));
        }

        if (await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(data.CarrierId), cancellationToken) is null)
        {
            return CarrierCostRefused.Because<RecordCarrierCostResult>(CarrierCostRefusal.CarrierNotFound(data.CarrierId.Value));
        }

        var at = time.GetUtcNow();
        var (cost, refusal) = CarrierCost.Record(data, at);
        if (cost is null)
        {
            return CarrierCostRefused.Because<RecordCarrierCostResult>(
                refusal ?? throw new InvalidOperationException($"Carrier cost {data.CarrierBookingNumber} was neither recorded nor refused."));
        }

        foreach (var invoice in await invoices.ListAsync(new CarrierInvoicesOfCarrierToDecideSpec(data.CarrierId), cancellationToken))
        {
            // The first invoice to claim the reference gets the Carrier cost; a later one finds it Invoiced and is a Duplicate.
            if (invoice.MatchAgain(cost, tolerance.Amount, at))
            {
                outbox.Add(CarrierInvoiceApprovedV1.Of(invoice, Guid.CreateVersion7(), at));
            }
        }

        await costs.AddAsync(cost, cancellationToken);
        return new RecordCarrierCostResult(CarrierCostViews.Of(cost), Repeat: false);
    }
}
