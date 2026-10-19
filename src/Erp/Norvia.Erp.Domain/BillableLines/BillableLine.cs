using Ardalis.SharedKernel;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>
/// One Billable Transport Order, as the TMS handed it over. It holds what <c>TransportOrderBillable</c> carries and is Waiting until
/// an Invoice run invoices it. The ERP never recalculates the Freight Charge.
/// </summary>
public sealed class BillableLine : EntityBase<BillableLineId>, IAggregateRoot
{
    private BillableLine()
    {
    }

    private BillableLine(BillableLineData data, DateTimeOffset recordedAt)
    {
        Id = new BillableLineId(data.TransportOrderNumber);
        ShipperId = data.ShipperId;
        ShipperReference = data.ShipperReference;
        ShipmentNumber = data.ShipmentNumber;
        DeliveryTime = data.DeliveryTime;
        FreightCharge = data.FreightCharge;
        ReleasedWithoutProofOfDelivery = data.ReleasedWithoutProofOfDelivery;
        IntakeCorrelationId = data.IntakeCorrelationId;
        BillingCorrelationId = data.BillingCorrelationId;
        RecordedAt = recordedAt;
        Status = BillableLineStatus.Waiting;
    }

    public ShipperId ShipperId { get; private set; }

    public string ShipperReference { get; private set; } = string.Empty;

    public string ShipmentNumber { get; private set; } = string.Empty;

    /// <summary>When the Shipment was Delivered, with the offset of where it happened.</summary>
    public DateTimeOffset DeliveryTime { get; private set; }

    public FreightCharge FreightCharge { get; private set; } = null!;

    public bool ReleasedWithoutProofOfDelivery { get; private set; }

    /// <summary>The correlation ID of the Transport Order's intake, so it can be traced from intake to its Shipper invoice.</summary>
    public string IntakeCorrelationId { get; private set; } = string.Empty;

    /// <summary>The correlation ID of the transaction that made the Transport Order Billable.</summary>
    public string BillingCorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }

    public BillableLineStatus Status { get; private set; }

    /// <summary>The number of the Shipper invoice that invoiced the line; null while it is Waiting.</summary>
    public InvoiceNumber? InvoiceNumber { get; private set; }

    /// <summary>The line's total: the Freight Charge's total.</summary>
    public decimal Total => FreightCharge.Total;

    /// <summary>
    /// Records a Billable line as Waiting, or refuses it when its Freight Charge breaks the billing rules: it needs at least one
    /// charge line, and its total is the sum of its lines. Whether the Shipper exists is for the caller to check.
    /// </summary>
    public static (BillableLine? Line, BillableLineRefusal? Refusal) Record(BillableLineData data, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(data);

        var charge = data.FreightCharge;
        if (charge.ChargeLines.Count == 0)
        {
            return (null, BillableLineRefusal.NoChargeLines(data.TransportOrderNumber));
        }

        var sum = charge.ChargeLines.Sum(line => line.Amount);
        if (sum != charge.Total)
        {
            return (null, BillableLineRefusal.TotalIsNotTheSumOfTheLines(data.TransportOrderNumber, charge.Total, sum));
        }

        return (new BillableLine(data, recordedAt), null);
    }

    /// <summary>Sets a Waiting line to Invoiced with the invoice's number. A line is invoiced once and only once.</summary>
    public void Invoice(InvoiceNumber invoiceNumber)
    {
        if (Status != BillableLineStatus.Waiting)
        {
            throw new InvalidOperationException($"Billable line {Id} is already invoiced.");
        }

        Status = BillableLineStatus.Invoiced;
        InvoiceNumber = invoiceNumber;
    }

    /// <summary>
    /// Whether a repeat carries the same content as this line. The correlation IDs of the message are left out: a Logic App's retry
    /// is the same Billable Transport Order whichever run delivers it.
    /// </summary>
    public bool HasSameContentAs(BillableLineData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Id.TransportOrderNumber == data.TransportOrderNumber
            && ShipperId == data.ShipperId
            && ShipperReference == data.ShipperReference
            && ShipmentNumber == data.ShipmentNumber
            && DeliveryTime.UtcDateTime == data.DeliveryTime.UtcDateTime
            && DeliveryTime.Offset == data.DeliveryTime.Offset
            && ReleasedWithoutProofOfDelivery == data.ReleasedWithoutProofOfDelivery
            && FreightCharge.Equals(data.FreightCharge);
    }
}
