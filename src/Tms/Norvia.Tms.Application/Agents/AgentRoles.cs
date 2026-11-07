namespace Norvia.Tms.Application.Agents;

/// <summary>The app roles of the Customer service agent's identity on the TMS API (ADR 0040). The estate Bicep defines them.</summary>
public static class AgentRoles
{
    /// <summary>Read the narrow Customer service agent view of a Rejected booking: no Consignee details and no file contents.</summary>
    public const string CustomerServiceRead = "TransportOrders.CustomerServiceAgent.Read";

    /// <summary>
    /// Read the Shipper's view of a Transport Order and nothing else: the Customer service reply agent's role (ADR 0011, ADR 0040). A
    /// role of its own, so the reply agent's identity cannot call the tools of <see cref="CustomerServiceRead"/>, and the other way round.
    /// </summary>
    public const string CustomerServiceReplyRead = "TransportOrders.CustomerServiceReplyAgent.Read";
}
