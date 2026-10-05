using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>Words for a Carrier Persona's tracking mode.</summary>
public static class TrackingModeText
{
    public static string Of(TrackingMode mode) => mode switch
    {
        TrackingMode.Manual => "Manual",
        TrackingMode.AutoProgress => "Auto-progress",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Every mode has words."),
    };
}
