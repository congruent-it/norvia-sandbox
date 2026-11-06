namespace Norvia.Tms.Api.Agents;

/// <summary>
/// The tokens the agent view accepts (ADR 0040). Locally they are signed with the AppHost's key (<c>local-token-signing-key</c>), as the
/// other local tokens are. In Azure there is no key: the tokens are Entra's, issued to API Management's identity for the TMS agent API's
/// app registration, and the issuer's metadata gives the keys. Unset, the view takes no token at all.
/// </summary>
public sealed class AgentApiOptions
{
    public const string SectionName = "Agents:Api";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded. Empty in Azure, where Entra's keys are used.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// In Azure, API Management's identity, the one caller of the agent view. Its token's <c>azp</c> is then the gateway, not the Agent,
    /// so the Agent's identity is read from the <c>X-Agent-ID</c> header the gateway's policy sets (it overrides the caller's own). Only
    /// a token from this client may name the Agent that way; locally it is empty and the token's own <c>azp</c> is the Agent.
    /// </summary>
    public string GatewayClientId { get; set; } = string.Empty;
}
