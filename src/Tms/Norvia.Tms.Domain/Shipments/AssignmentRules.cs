using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// P3's assignment rules, checked before a Transport Order is planned, so a Planner never plans onto a Carrier Norvia
/// doesn't use or plans the same Transport Order twice, and before a Shipment is reassigned or released, so what has been
/// handed over stays handed over. There are no coverage rules for Own fleet, and the rating result never blocks planning.
/// </summary>
public static class AssignmentRules
{
    public static AssignmentRefusal TransportOrderNotFound(string transportOrderNumber) =>
        new(AssignmentRefusalCode.TransportOrderNotFound, Invariant($"Transport Order {transportOrderNumber} is not known to the TMS."));

    public static AssignmentRefusal ShipmentNotFound(string shipmentNumber) =>
        new(AssignmentRefusalCode.ShipmentNotFound, Invariant($"Shipment {shipmentNumber} is not known to the TMS."));

    /// <summary>Returns the first rule the assignment breaks, the Transport Order's before the Carrier's, or null when it breaks none.</summary>
    /// <param name="carrierId">The Carrier chosen; null for Own fleet.</param>
    /// <param name="carrier">The TMS's Carrier data for the chosen Carrier, or null when it has none.</param>
    public static AssignmentRefusal? Check(TransportOrder order, CarrierId? carrierId, Carrier? carrier)
    {
        ArgumentNullException.ThrowIfNull(order);
        return CheckTransportOrder(order) ?? CheckCarrier(carrierId, carrier);
    }

    /// <summary>
    /// Returns the first rule the reassignment breaks, the Shipment's, then the Carrier's, then whether it changes anything,
    /// or null when it breaks none.
    /// </summary>
    /// <param name="carrierId">The Carrier chosen; null for Own fleet.</param>
    /// <param name="carrier">The TMS's Carrier data for the chosen Carrier, or null when it has none.</param>
    public static AssignmentRefusal? CheckReassignment(Shipment shipment, CarrierId? carrierId, Carrier? carrier)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return CheckShipment(shipment, PlanningAction.Reassignment) ?? CheckCarrier(carrierId, carrier) ?? CheckChange(shipment, carrierId, carrier);
    }

    /// <summary>Returns the rule the release breaks, or null when it breaks none: only a Planned Shipment can be released.</summary>
    public static AssignmentRefusal? CheckRelease(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return CheckShipment(shipment, PlanningAction.Release);
    }

    /// <summary>
    /// Prices the Carrier booking a release creates from the Carrier's Carrier Rate Card (P9), by P2's steps against the
    /// Carrier Rate Card instead of the Shipper's Rate Card: its Carrier cost, or the refusal naming the Carrier and why the
    /// Carrier Rate Card can't price it, with the same codes as Rating's failures.
    /// </summary>
    /// <param name="carrierRateCard">The Carrier's Carrier Rate Card, or null when it has none.</param>
    public static (CarrierCost? Cost, AssignmentRefusal? Refusal) PriceCarrierBooking(
        Carrier carrier, CarrierRateCard? carrierRateCard, TransportOrderDetails details, DateTimeOffset calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(details);
        var outcome = carrierRateCard?.Rate(details)
            ?? RatingOutcome.Failed(RatingFailureCode.NoRateCard, Invariant($"{Describe(carrier.Id, carrier)} has no Carrier Rate Card."));
        if (outcome.FreightCharge is { } priced)
        {
            return (CarrierCost.From(priced, calculatedAt), null);
        }

        var failure = outcome.Failure!;
        return (null, new(
            failure.Code switch
            {
                RatingFailureCode.NoRateCard => AssignmentRefusalCode.NoRateCard,
                RatingFailureCode.NoValidRateCardVersion => AssignmentRefusalCode.NoValidRateCardVersion,
                RatingFailureCode.NoMatchingLane => AssignmentRefusalCode.NoMatchingLane,
                RatingFailureCode.WeightAboveHighestBreak => AssignmentRefusalCode.WeightAboveHighestBreak,
                _ => throw new ArgumentOutOfRangeException(nameof(carrierRateCard), failure.Code, "A rating failure with no release refusal code."),
            },
            Invariant($"{Describe(carrier.Id, carrier)} can't price the Carrier booking. {failure.Message}")));
    }

    /// <summary>
    /// Prices the Carrier booking from a Spot price the Planner entered (P9), which is allowed only after the Carrier Rate
    /// Card failed to price the Shipment: the Planner can't override a Carrier Rate Card that prices it.
    /// </summary>
    /// <param name="carrierRateCardRefusal">Why the Carrier Rate Card can't price the Shipment, or null when it can.</param>
    public static (CarrierCost? Cost, AssignmentRefusal? Refusal) PriceCarrierBookingAtSpot(
        Carrier carrier, AssignmentRefusal? carrierRateCardRefusal, Money amount, string remark, DateTimeOffset calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        return carrierRateCardRefusal is null
            ? (null, new(
                AssignmentRefusalCode.CarrierRateCardPricesShipment,
                Invariant($"{Describe(carrier.Id, carrier)}'s Carrier Rate Card prices this Shipment, so it can't be released with a Spot price.")))
            : (CarrierCost.Spot(amount, remark, calculatedAt), null);
    }

    /// <summary>The refusal for a Spot price on a Shipment of Own fleet, which has no Carrier cost.</summary>
    public static AssignmentRefusal SpotPriceForOwnFleet(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return new(AssignmentRefusalCode.SpotPriceNotAllowed, Invariant($"Shipment {shipment.Number} is carried by Own fleet, which has no Carrier cost, so it can't be released with a Spot price."));
    }

    private static AssignmentRefusal? CheckShipment(Shipment shipment, PlanningAction action) => shipment.Status == ShipmentStatus.Planned
        ? null
        : new(
            AssignmentRefusalCode.ShipmentNotPlanned,
            Invariant($"Shipment {shipment.Number} is {shipment.Status}. Only a Planned Shipment can be {action.Done()}."));

    // carrier is the TMS's data for carrierId, already resolved and validated by CheckCarrier above.
    private static AssignmentRefusal? CheckChange(Shipment shipment, CarrierId? carrierId, Carrier? carrier) => shipment.CarriedBy.CarrierId == carrierId
        ? new(AssignmentRefusalCode.AssignmentUnchanged, Invariant($"Shipment {shipment.Number} is assigned to {Describe(carrierId, carrier)} already."))
        : null;

    private static AssignmentRefusal? CheckTransportOrder(TransportOrder order) => order.Status switch
    {
        TransportOrderStatus.Accepted => null,
        TransportOrderStatus.Planned => new(
            AssignmentRefusalCode.TransportOrderAlreadyPlanned,
            Invariant($"Transport Order {order.Number} is Planned already, as Shipment {order.ShipmentNumber}. A Transport Order has exactly one Shipment.")),
        _ => new(
            AssignmentRefusalCode.TransportOrderNotAccepted,
            Invariant($"The Transport Order is {order.Status}. Only an Accepted Transport Order can be assigned.")),
    };

    private static AssignmentRefusal? CheckCarrier(CarrierId? carrierId, Carrier? carrier)
    {
        if (carrierId is null)
        {
            return null;
        }

        if (carrier is null)
        {
            return new(AssignmentRefusalCode.UnknownCarrier, Invariant($"Carrier {carrierId} is not known to the TMS."));
        }

        return carrier.Active
            ? null
            : new(AssignmentRefusalCode.InactiveCarrier, Invariant($"{Describe(carrierId, carrier)} is not active, so it can't be chosen."));
    }

    // A Carrier by name, not just its ID, so a refusal reads for a Planner who may not know the ID by heart.
    private static string Describe(CarrierId? carrierId, Carrier? carrier) =>
        carrierId is null ? "Own fleet" : Invariant($"Carrier {carrierId} ({carrier!.Name})");
}
