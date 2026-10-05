using Norvia.Tms.Domain.Shipments;
using static System.FormattableString;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>P6's Proof of delivery rules, apart from finding the Shipment by the Carrier's booking reference.</summary>
public static class ProofOfDeliveryRules
{
    public static ProofOfDeliveryRefusal ShipmentNotFound(string carrierBookingReference) =>
        new(ProofOfDeliveryRefusalCode.ShipmentNotFound, Invariant($"No Confirmed Carrier booking of this Carrier has the booking reference {carrierBookingReference}."));

    /// <summary>The refusal for a Shipment that isn't Booked or later, or null when it is.</summary>
    public static ProofOfDeliveryRefusal? CheckShipment(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return shipment.Status >= ShipmentStatus.Booked
            ? null
            : new(ProofOfDeliveryRefusalCode.ShipmentNotInTransport, Invariant($"Shipment {shipment.Number} is {shipment.Status}, not Booked or later."));
    }
}
