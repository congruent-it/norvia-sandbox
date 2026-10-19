using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Application.BillableLines;

internal static class BillableLineViews
{
    public static BillableLineView Of(BillableLine line) => new(
        line.Id.TransportOrderNumber,
        line.ShipperId.Value,
        line.ShipperReference,
        line.ShipmentNumber,
        line.DeliveryTime,
        new FreightChargeView(
            FreightCharge.Currency,
            line.FreightCharge.Total,
            [.. line.FreightCharge.ChargeLines.Select(chargeLine => new ChargeLineView(chargeLine.Type.ToString(), chargeLine.Amount))],
            line.FreightCharge.RateCardId,
            line.FreightCharge.RateCardVersion),
        line.ReleasedWithoutProofOfDelivery,
        line.IntakeCorrelationId,
        line.BillingCorrelationId,
        line.RecordedAt,
        line.Status.ToString(),
        line.InvoiceNumber?.Value);
}
