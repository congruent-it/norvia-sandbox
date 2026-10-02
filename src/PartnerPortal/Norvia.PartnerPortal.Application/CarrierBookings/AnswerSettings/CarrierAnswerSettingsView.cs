using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

/// <summary>How a Carrier Persona answers the booking requests that reach it from now on.</summary>
public sealed record CarrierAnswerSettingsView(CarrierAnswerMode Mode, CarrierRejectionReason RejectionReason, int DelaySeconds)
{
    public static CarrierAnswerSettingsView From(CarrierAnswerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy.Mode, policy.RejectionReason, (int)policy.Delay.TotalSeconds);
    }
}
