namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>The stable codes of the Carrier cost rules. The names are part of the ERP billing API's contract.</summary>
public enum CarrierCostRefusalCode
{
    CarrierNotFound,
    CarrierCostConflict,
    CarrierCostWithoutChargeLines,
    CarrierCostTotalMismatch,
}
