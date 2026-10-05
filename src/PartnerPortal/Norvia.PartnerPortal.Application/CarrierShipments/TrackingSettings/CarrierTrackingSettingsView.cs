using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;

/// <summary>How a Carrier Persona sends the Tracking events of the Shipments it confirms from now on.</summary>
public sealed record CarrierTrackingSettingsView(TrackingMode Mode, int DelaySeconds)
{
    public static CarrierTrackingSettingsView From(CarrierTrackingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy.Mode, (int)policy.Delay.TotalSeconds);
    }
}
