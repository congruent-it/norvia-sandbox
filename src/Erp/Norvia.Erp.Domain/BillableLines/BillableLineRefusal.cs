using static System.FormattableString;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>Why the ERP refused to record a Billable line: a stable code and a readable message.</summary>
public sealed record BillableLineRefusal(BillableLineRefusalCode Code, string Message)
{
    public static BillableLineRefusal ShipperNotFound(string shipperId) =>
        new(BillableLineRefusalCode.ShipperNotFound, Invariant($"The ERP has no Shipper {shipperId}."));

    public static BillableLineRefusal Conflict(string transportOrderNumber) =>
        new(
            BillableLineRefusalCode.BillableLineConflict,
            Invariant($"The ERP already has a Billable line for Transport Order {transportOrderNumber} with different content."));

    public static BillableLineRefusal NoChargeLines(string transportOrderNumber) =>
        new(BillableLineRefusalCode.FreightChargeWithoutChargeLines, Invariant($"The Freight Charge of Transport Order {transportOrderNumber} has no charge lines."));

    public static BillableLineRefusal TotalIsNotTheSumOfTheLines(string transportOrderNumber, decimal total, decimal sum) =>
        new(
            BillableLineRefusalCode.FreightChargeTotalMismatch,
            Invariant($"The Freight Charge total {total} of Transport Order {transportOrderNumber} isn't the sum of its charge lines, {sum}."));
}
