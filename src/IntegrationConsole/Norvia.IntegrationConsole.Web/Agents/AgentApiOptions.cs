using System.ComponentModel.DataAnnotations;

namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>
/// The tokens the agent API accepts (ADR 0040). Locally they are signed with the AppHost's key (<c>local-token-signing-key</c>),
/// as the other local tokens are. Entra's own tokens are validated by the Azure slice (#380), so until then the API
/// is mapped only where this key is set.
/// </summary>
public sealed class AgentApiOptions
{
    public const string SectionName = "Agents:Api";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;
}
