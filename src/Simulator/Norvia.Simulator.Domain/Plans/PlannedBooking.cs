using Norvia.Simulator.Domain.Planner;

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

    public FleetLoad Load => new(TotalWeightKg, PalletPlaces);

    public decimal TotalWeightKg => Goods.Sum(line => line.WeightKg);

    /// <summary>What the order asks of the Own fleet's pallet places: the quantity of its pallet lines, as the TMS's load check counts it (P10).</summary>
    public int PalletPlaces => Goods.Where(line => line.Package == PackageKind.Pallet).Sum(line => line.Quantity);
}
