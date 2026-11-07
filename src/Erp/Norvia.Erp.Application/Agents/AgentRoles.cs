namespace Norvia.Erp.Application.Agents;

/// <summary>The app roles of the Agents' identities on the ERP API (ADR 0040), one for each Agent. The estate Bicep defines them.</summary>
public static class AgentRoles
{
    /// <summary>Read the narrow Finance agent view of a Carrier invoice line: no Planner remark and no personal data.</summary>
    public const string FinanceRead = "CarrierInvoices.FinanceAgent.Read";

    /// <summary>
    /// Read what the Carrier can see of its own Carrier invoice line, for the rejection reason agent. A role of its own, so that Agent's
    /// identity cannot call the Finance agent view and the Carrier invoice matching agent cannot call this one (#473).
    /// </summary>
    public const string FinanceRejectionReasonRead = "CarrierInvoices.FinanceRejectionReasonAgent.Read";
}
