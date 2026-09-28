using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// P3's assignment rules, checked before a Transport Order is planned, so a Planner never plans onto a Carrier Norvia
/// doesn't use or plans the same Transport Order twice, and before a Planned Shipment is reassigned. There are no coverage
/// rules for Own fleet, and the rating result never blocks planning.
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
        return CheckShipment(shipment) ?? CheckCarrier(carrierId, carrier) ?? CheckChange(shipment, carrierId);
    }

    private static AssignmentRefusal? CheckShipment(Shipment shipment) => shipment.Status == ShipmentStatus.Planned
        ? null
        : new(
            AssignmentRefusalCode.ShipmentNotPlanned,
            Invariant($"Shipment {shipment.Number} is {shipment.Status}. Only a Planned Shipment can be reassigned."));

    private static AssignmentRefusal? CheckChange(Shipment shipment, CarrierId? carrierId) => shipment.CarriedBy.CarrierId == carrierId
        ? new(AssignmentRefusalCode.AssignmentUnchanged, Invariant($"Shipment {shipment.Number} is assigned to {shipment.CarriedBy} already."))
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
            : new(AssignmentRefusalCode.InactiveCarrier, Invariant($"Carrier {carrierId} ({carrier.Name}) is not active, so it can't be chosen."));
    }
}
