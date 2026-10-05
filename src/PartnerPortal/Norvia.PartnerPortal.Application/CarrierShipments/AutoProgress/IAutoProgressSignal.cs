namespace Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;

/// <summary>Tells whoever sends Tracking events automatically that a Shipment with automatic events has been confirmed, so it looks at the clock again.</summary>
public interface IAutoProgressSignal
{
    void ShipmentConfirmed();
}
