namespace Norvia.PartnerPortal.Domain.CarrierBookings;

public readonly record struct CarrierTrackingSettingsId(Guid Value)
{
    public static CarrierTrackingSettingsId New() => new(Guid.CreateVersion7());
}
