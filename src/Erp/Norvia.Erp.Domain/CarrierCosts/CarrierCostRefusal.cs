using static System.FormattableString;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>Why the ERP refused to record a Carrier cost: a stable code and a readable message.</summary>
public sealed record CarrierCostRefusal(CarrierCostRefusalCode Code, string Message)
{
    public static CarrierCostRefusal CarrierNotFound(string carrierId) =>
        new(CarrierCostRefusalCode.CarrierNotFound, Invariant($"The ERP has no Carrier {carrierId}."));

    public static CarrierCostRefusal Conflict(string carrierBookingNumber) =>
        new(
            CarrierCostRefusalCode.CarrierCostConflict,
            Invariant($"The ERP already has a Carrier cost for Carrier booking {carrierBookingNumber} with different content."));

    public static CarrierCostRefusal NoChargeLines(string carrierBookingNumber) =>
        new(CarrierCostRefusalCode.CarrierCostWithoutChargeLines, Invariant($"The Carrier cost of Carrier booking {carrierBookingNumber} has no charge lines."));

    public static CarrierCostRefusal TotalIsNotTheSumOfTheLines(string carrierBookingNumber, decimal total, decimal sum) =>
        new(
            CarrierCostRefusalCode.CarrierCostTotalMismatch,
            Invariant($"The Carrier cost total {total} of Carrier booking {carrierBookingNumber} isn't the sum of its charge lines, {sum}."));
}
