using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Application.CarrierCosts;

internal static class CarrierCostViews
{
    public static CarrierCostView Of(CarrierCost cost) => new(
        cost.Id.CarrierBookingNumber,
        cost.CarrierBookingReference,
        cost.CarrierId.Value,
        cost.ShipmentNumber,
        cost.TransportOrderNumber,
        CarrierCostCharge.Currency,
        cost.Charge.Total,
        [.. cost.Charge.ChargeLines.Select(line => new ChargeLineView(line.Type.ToString(), line.Amount))],
        new CarrierCostSourceView(cost.Source.Kind.ToString(), cost.Source.RateCardId, cost.Source.RateCardVersion, cost.Source.Remark),
        cost.DeliveryTime,
        cost.CorrelationId,
        cost.RecordedAt,
        cost.Status.ToString());
}
