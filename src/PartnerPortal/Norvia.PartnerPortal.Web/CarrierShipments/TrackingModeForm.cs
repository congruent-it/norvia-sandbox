using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>The tracking mode form's model: the mode, and the delay before each automatic Tracking event.</summary>
public sealed class TrackingModeForm
{
    public const int LongestDelaySeconds = 120;
    public const string DelayOutOfRange = "Enter a delay between 0 and 120 seconds.";

    public TrackingMode Mode { get; set; }

    [Range(0, LongestDelaySeconds, ErrorMessage = DelayOutOfRange)]
    public int DelaySeconds { get; set; }

    public static TrackingModeForm From(CarrierTrackingSettingsView settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new() { Mode = settings.Mode, DelaySeconds = settings.DelaySeconds };
    }

    public CarrierTrackingSettingsView ToSettings() => new(Mode, DelaySeconds);
}
