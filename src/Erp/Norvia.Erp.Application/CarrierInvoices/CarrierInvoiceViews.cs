using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices;

internal static class CarrierInvoiceViews
{
    public static CarrierInvoiceView Of(CarrierInvoice invoice) => new(
        invoice.CarrierId.Value,
        invoice.InvoiceNumber,
        invoice.InvoiceDate,
        CarrierCostCharge.Currency,
        invoice.Total,
        [
            .. invoice.Lines.Select(line => new CarrierInvoiceLineView(
                line.CarrierBookingReference,
                line.Amount,
                line.Description,
                line.Result.ToString(),
                line.Status.ToString(),
                line.CarrierBookingNumber,
                line.AcceptedAmount,
                line.Remark)),
        ],
        invoice.CorrelationId,
        invoice.ReceivedAt,
        invoice.Status.ToString(),
        invoice.ApprovedAmount,
        invoice.ApprovedAt);
}
