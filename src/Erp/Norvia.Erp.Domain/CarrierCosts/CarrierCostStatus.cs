namespace Norvia.Erp.Domain.CarrierCosts;

public enum CarrierCostStatus
{
    /// <summary>No Carrier invoice line is Matched or Accepted against the Carrier cost yet.</summary>
    Open,

    /// <summary>A Carrier invoice line was Matched or Accepted against the Carrier cost.</summary>
    Invoiced,
}
