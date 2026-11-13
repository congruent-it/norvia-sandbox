using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

/// <summary>
/// The Shipper tracking assistant's settings (ADR 0049). <c>Agents:Enabled</c> is off by default and the AppHost switches it on locally; the limits are
/// the estate's parameters, <c>Agents:Limits</c>, and default to the ADR's numbers.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }

    /// <summary>How long a run may take before the Shipper is told the assistant is unavailable.</summary>
    [Range(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>The Foundry project the assistant is published in (the Intake project, whose prompts are not traced), which the portal calls as its own identity.</summary>
    public FoundryOptions Foundry { get; set; } = new();

    /// <summary>The Entra client ID of the portal's managed identity, which asks the Agent in Azure.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Whether the Agent runs in Foundry, which is the case wherever the estate gave the portal a project endpoint.</summary>
    public bool UsesFoundry => Foundry.ProjectEndpoint is not null;

    [ValidateObjectMembers]
    public AssistantLimits Limits { get; set; } = new();
}
