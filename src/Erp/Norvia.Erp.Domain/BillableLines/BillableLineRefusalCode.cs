namespace Norvia.Erp.Domain.BillableLines;

/// <summary>The stable codes of the billing rules. The names are part of the ERP billing API's contract.</summary>
public enum BillableLineRefusalCode
{
    ShipperNotFound,
    BillableLineConflict,
    FreightChargeWithoutChargeLines,
    FreightChargeTotalMismatch,
}
