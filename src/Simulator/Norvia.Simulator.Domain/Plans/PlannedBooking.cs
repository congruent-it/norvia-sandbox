namespace Norvia.Simulator.Domain.Plans;

/// <summary>
/// One booking of a Simulation plan, sent to the Partner API as its Shipper's Persona. A bad booking leaves out or breaks
/// something on purpose (<see cref="Bad"/>); the fields it leaves out are null or empty.
/// </summary>
public sealed record PlannedBooking(
    int Step,
    string PersonaId,
    string PersonaName,
    string ShipperId,
    string ShipperName,
    string Reference,
    BadBookingKind? Bad,
    string PickupZone,
    string DeliveryZone,
    PlannedAddress Pickup,
    PlannedAddress Delivery,
    PickupSlot Slot,
    string? ConsigneeName,
    string? ConsigneeContact,
    string? ConsigneePhone,
    IReadOnlyList<PlannedGoodsLine> Goods,
    string? Remarks)
{
    public ExpectedAnswer Expected => Bad switch
    {
        BadBookingKind.RepeatedReference => ExpectedAnswer.AlreadyBooked,
        BadBookingKind.ConflictingReference or BadBookingKind.InvalidContent => ExpectedAnswer.Rejected,
        _ => ExpectedAnswer.Accepted,
    };

    public decimal TotalWeightKg => Goods.Sum(line => line.WeightKg);
}
