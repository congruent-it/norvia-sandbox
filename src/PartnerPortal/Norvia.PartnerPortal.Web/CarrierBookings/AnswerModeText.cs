using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>Words for a Carrier Persona's answer mode, and for the automatic answer a request waits for.</summary>
public static class AnswerModeText
{
    public static string Of(CarrierAnswerMode mode) => mode switch
    {
        CarrierAnswerMode.Manual => "Manual",
        CarrierAnswerMode.AutoConfirm => "Auto-confirm",
        CarrierAnswerMode.AutoReject => "Auto-reject",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Every mode has words."),
    };

    /// <summary>What an open request is waiting for, such as "Auto-confirms at 11:00:05"; null when nobody will answer it for the Carrier.</summary>
    public static string? Waiting(CarrierBookingRequestView request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IsOpen || request.AutoAnswerMode is not { } mode || request.AutoAnswerDueAt is not { } dueAt)
        {
            return null;
        }

        var verb = mode == CarrierAnswerMode.AutoConfirm ? "Auto-confirms" : "Auto-rejects";
        return $"{verb} at {PortalTime.FormatTimeOfDay(dueAt)}";
    }
}
