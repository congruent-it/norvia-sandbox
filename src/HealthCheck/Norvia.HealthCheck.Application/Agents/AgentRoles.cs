namespace Norvia.HealthCheck.Application.Agents;

/// <summary>The app roles the Agents' identities hold on the health-check API (ADR 0040). The estate Bicep defines them (health-check-mcp.bicep). An Agent holds one of them and no Azure role.</summary>
public static class AgentRoles
{
    /// <summary>Call the health-check API's one read operation, <c>run_checks</c>.</summary>
    public const string Read = "HealthCheck.Agent.Read";

    /// <summary>The FinOps agent's call role (#511): the two cost tools, <c>get_cost_view</c> and <c>get_rising_spend_findings</c>, and nothing else. Not an operator's role of the spend page (<c>HealthCheck.Cost</c>).</summary>
    public const string CostRead = "HealthCheck.Cost.Read";
}
