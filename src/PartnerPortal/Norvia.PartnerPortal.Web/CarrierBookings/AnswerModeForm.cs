using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>The answer mode form's model: the mode, the reason an auto-reject gives, and the delay before an automatic answer.</summary>
public sealed class AnswerModeForm
{
    public const int LongestDelaySeconds = 120;
    public const string DelayOutOfRange = "Enter a delay between 0 and 120 seconds.";

    public CarrierAnswerMode Mode { get; set; }

    public CarrierRejectionReason Reason { get; set; } = CarrierRejectionReason.NoCapacity;

    [Range(0, LongestDelaySeconds, ErrorMessage = DelayOutOfRange)]
    public int DelaySeconds { get; set; }

    public static AnswerModeForm From(CarrierAnswerSettingsView settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new() { Mode = settings.Mode, Reason = settings.RejectionReason, DelaySeconds = settings.DelaySeconds };
    }

    public CarrierAnswerSettingsView ToSettings() => new(Mode, Reason, DelaySeconds);
}
