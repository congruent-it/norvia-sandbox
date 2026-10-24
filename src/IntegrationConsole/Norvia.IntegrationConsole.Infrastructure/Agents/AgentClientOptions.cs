using System.ComponentModel.DataAnnotations;

namespace Norvia.IntegrationConsole.Infrastructure.Agents;

/// <summary>
/// How the console reaches an Agent (ADR 0040, ADR 0041). In Azure the Foundry client asks the Agent published in the project at
/// <see cref="FoundryOptions.ProjectEndpoint"/>. Without a project endpoint the console runs the Agent in process: the MCP server to
/// call, the token to call it with (signed with the AppHost's local key, as the other local tokens are), and optionally a model
/// deployment of the developer's own. Either way a run may take only so long.
/// </summary>
public sealed class AgentClientOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }

    /// <summary>The MCP stand-in's <c>/mcp</c> endpoint.</summary>
    public Uri? McpAddress { get; set; }

    /// <summary>A short timeout and no retry: an Agent is never on a critical path (ADR 0040).</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 20;

    public AgentTokenOptions Tokens { get; set; } = new();

    public AgentModelOptions Model { get; set; } = new();

    public FoundryOptions Foundry { get; set; } = new();

    /// <summary>Whether the Agents run in Foundry, which is the case wherever the estate gave the console a project endpoint.</summary>
    public bool UsesFoundry => Foundry.ProjectEndpoint is not null;
}

/// <summary>The Foundry project the Agents are published in (ADR 0040), which the console calls as its own managed identity.</summary>
public sealed class FoundryOptions
{
    /// <summary>The project's endpoint, such as <c>https://&lt;account&gt;.services.ai.azure.com/api/projects/norvia</c>.</summary>
    public Uri? ProjectEndpoint { get; set; }

    /// <summary>The scope of the token the project takes.</summary>
    public string Scope { get; set; } = "https://ai.azure.com/.default";
}

/// <summary>The Agent's local token: the identity it is, the role it holds and the key the MCP stand-in trusts.</summary>
public sealed class AgentTokenOptions
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>The Agent's identity, the token's <c>azp</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience) && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(Role) && !string.IsNullOrWhiteSpace(SigningKey);
}

/// <summary>
/// Points the runner at a developer's own model deployment in Foundry for a real local demo. Without an endpoint the model is
/// the scripted fake, so a local run and CI are deterministic and need no Azure.
/// </summary>
public sealed class AgentModelOptions
{
    public Uri? Endpoint { get; set; }

    /// <summary>The deployment's name; the definition's own is used when empty.</summary>
    public string? Deployment { get; set; }
}
