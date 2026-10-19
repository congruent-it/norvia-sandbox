using Ardalis.SharedKernel;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>
/// What Norvia pays a Carrier for one Carrier booking, as the TMS handed it over once the Shipment was Delivered. It is Open until a
/// Carrier invoice line is Matched or Accepted against it. The ERP never recalculates the charge.
/// </summary>
public sealed class CarrierCost : EntityBase<CarrierCostId>, IAggregateRoot
{
    private CarrierCost()
    {
    }

    private CarrierCost(CarrierCostData data, DateTimeOffset recordedAt)
    {
        Id = new CarrierCostId(data.CarrierBookingNumber);
        CarrierBookingReference = data.CarrierBookingReference;
        CarrierId = data.CarrierId;
        ShipmentNumber = data.ShipmentNumber;
        TransportOrderNumber = data.TransportOrderNumber;
        Charge = data.Charge;
        Source = data.Source;
        DeliveryTime = data.DeliveryTime;
        CorrelationId = data.CorrelationId;
        RecordedAt = recordedAt;
        Status = CarrierCostStatus.Open;
    }

    /// <summary>The Carrier's own reference for the booking, which a Carrier invoice line quotes.</summary>
    public string CarrierBookingReference { get; private set; } = string.Empty;

    public CarrierId CarrierId { get; private set; }

    public string ShipmentNumber { get; private set; } = string.Empty;

    public string TransportOrderNumber { get; private set; } = string.Empty;

    public CarrierCostCharge Charge { get; private set; } = null!;

    public CarrierCostSource Source { get; private set; } = null!;

    /// <summary>When the Shipment was Delivered, with the offset of where it happened.</summary>
    public DateTimeOffset DeliveryTime { get; private set; }

    /// <summary>The correlation ID <c>CarrierCostIncurred</c> carried, which the Carrier cost keeps.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }

    public CarrierCostStatus Status { get; private set; }

    /// <summary>The Carrier cost's total: the charge's total.</summary>
    public decimal Total => Charge.Total;

    /// <summary>
    /// Records a Carrier cost as Open, or refuses it when its charge breaks the rules: it needs at least one charge line, and its
    /// total is the sum of its lines. Whether the Carrier exists is for the caller to check.
    /// </summary>
    public static (CarrierCost? Cost, CarrierCostRefusal? Refusal) Record(CarrierCostData data, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(data);

        var charge = data.Charge;
        if (charge.ChargeLines.Count == 0)
        {
            return (null, CarrierCostRefusal.NoChargeLines(data.CarrierBookingNumber));
        }

        var sum = charge.ChargeLines.Sum(line => line.Amount);
        if (sum != charge.Total)
        {
            return (null, CarrierCostRefusal.TotalIsNotTheSumOfTheLines(data.CarrierBookingNumber, charge.Total, sum));
        }

        return (new CarrierCost(data, recordedAt), null);
    }

    /// <summary>
    /// Whether a repeat carries the same content as this Carrier cost. The correlation ID of the message is left out: a Logic App's
    /// retry is the same Carrier cost whichever run delivers it.
    /// </summary>
    public bool HasSameContentAs(CarrierCostData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Id.CarrierBookingNumber == data.CarrierBookingNumber
            && CarrierBookingReference == data.CarrierBookingReference
            && CarrierId == data.CarrierId
            && ShipmentNumber == data.ShipmentNumber
            && TransportOrderNumber == data.TransportOrderNumber
            && DeliveryTime.UtcDateTime == data.DeliveryTime.UtcDateTime
            && DeliveryTime.Offset == data.DeliveryTime.Offset
            && Charge.Equals(data.Charge)
            && Source == data.Source;
    }
}
