using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.CarrierCosts;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.List;

public sealed class ListCarrierInvoicesToDecideHandler(
    IReadRepository<CarrierInvoice> invoices,
    IReadRepository<CarrierCost> costs,
    IReadRepository<Carrier> carriers)
    : IQueryHandler<ListCarrierInvoicesToDecideQuery, IReadOnlyList<CarrierInvoiceToDecide>>
{
    public async ValueTask<IReadOnlyList<CarrierInvoiceToDecide>> Handle(ListCarrierInvoicesToDecideQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var received = await invoices.ListAsync(new CarrierInvoicesToDecideSpec(), cancellationToken);
        if (received.Count == 0)
        {
            return [];
        }

        var names = (await carriers.ListAsync(new CarriersSpec(), cancellationToken)).ToDictionary(carrier => carrier.Id, carrier => carrier.Name);
        var numbers = received.SelectMany(invoice => invoice.Lines).Select(line => line.CarrierBookingNumber).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var byNumber = numbers.Count == 0
            ? []
            : (await costs.ListAsync(new CarrierCostsByBookingNumbersSpec(numbers), cancellationToken)).ToDictionary(cost => cost.Id.CarrierBookingNumber, CarrierCostViews.Of);

        return
        [
            .. received.Select(invoice => new CarrierInvoiceToDecide(
                names.GetValueOrDefault(invoice.CarrierId, invoice.CarrierId.Value),
                CarrierInvoiceViews.Of(invoice),
                invoice.Lines
                    .Where(line => line.CarrierBookingNumber is not null && byNumber.ContainsKey(line.CarrierBookingNumber))
                    .Select(line => line.CarrierBookingNumber!)
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(number => number, number => byNumber[number]))),
        ];
    }
}
