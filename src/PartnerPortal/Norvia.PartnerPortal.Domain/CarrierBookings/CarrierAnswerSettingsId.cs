namespace Norvia.PartnerPortal.Domain.CarrierBookings;

public readonly record struct CarrierAnswerSettingsId(Guid Value)
{
    public static CarrierAnswerSettingsId New() => new(Guid.CreateVersion7());
}
