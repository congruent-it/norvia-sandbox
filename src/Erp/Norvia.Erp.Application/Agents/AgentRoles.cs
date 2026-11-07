namespace Norvia.Erp.Application.Agents;

/// <summary>The app role of the Carrier invoice matching agent's identity on the ERP API (ADR 0040). The estate Bicep defines it.</summary>
public static class AgentRoles
{
    /// <summary>Read the narrow Finance agent view of a Carrier invoice line: no Planner remark and no personal data.</summary>
    public const string FinanceRead = "CarrierInvoices.FinanceAgent.Read";
}
