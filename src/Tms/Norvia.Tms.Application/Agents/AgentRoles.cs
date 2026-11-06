namespace Norvia.Tms.Application.Agents;

/// <summary>The app roles of the Customer service agent's identity on the TMS API (ADR 0040). The estate Bicep defines them.</summary>
public static class AgentRoles
{
    /// <summary>Read the narrow Customer service agent view of a Rejected booking: no Consignee details and no file contents.</summary>
    public const string CustomerServiceRead = "TransportOrders.CustomerServiceAgent.Read";
}
