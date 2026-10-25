using System.ComponentModel.DataAnnotations;

namespace Norvia.HealthCheck.Web.Agents;

/// <summary>
/// The tokens the agent API accepts (ADR 0040). Locally they are signed with the AppHost's key (<c>local-token-signing-key</c>), as the
/// other local tokens are. In Azure there is no key: the tokens are Entra's, and the issuer's metadata gives the keys, for the tokens API Management sends for the app's agent API.
/// </summary>
public sealed class AgentApiOptions
{
    public const string SectionName = "Agents:Api";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded. Empty in Azure, where Entra's keys are used.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// In Azure, API Management's identity, the one caller of the agent API. Its token's <c>azp</c> is then the gateway, not the Agent,
    /// so the Agent's identity is read from the <c>X-Agent-ID</c> header the gateway's policy sets. Only a token from this client may name the
    /// Agent that way; locally it is empty and the token's own <c>azp</c> is the Agent.
    /// </summary>
    public string GatewayClientId { get; set; } = string.Empty;
}
