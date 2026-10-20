using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A Shipment a Carrier reported delivered, with the Carrier booking and the Carrier's own reference for it: what the Carrier can invoice.</summary>
internal sealed record CarrierDelivery(PlannedShipment Shipment, string CarrierBookingNumber, string BookingReference)
{
    public SimulatedCarrier Carrier => Shipment.Carrier;
}
