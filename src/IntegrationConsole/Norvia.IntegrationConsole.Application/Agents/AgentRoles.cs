namespace Norvia.IntegrationConsole.Application.Agents;

/// <summary>The app role an Agent's identity holds on the console's agent API (ADR 0040). The estate Bicep defines it.</summary>
public static class AgentRoles
{
    /// <summary>Read the narrow agent view: Message trails and Logic App runs without any message body.</summary>
    public const string Read = "IntegrationConsole.Agent.Read";
}
