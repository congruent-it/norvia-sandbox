namespace Norvia.HealthCheck.Application.Agents;

/// <summary>The app role an Agent's identity holds on the health-check API (ADR 0040). The estate Bicep defines it (#392). It is the only role the Agent holds: no Azure role.</summary>
public static class AgentRoles
{
    /// <summary>Call the health-check API's one read operation, <c>run_checks</c>.</summary>
    public const string Read = "HealthCheck.Agent.Read";
}
