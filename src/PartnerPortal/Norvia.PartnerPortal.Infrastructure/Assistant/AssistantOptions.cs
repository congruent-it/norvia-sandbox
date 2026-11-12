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
    public int TimeoutSeconds { get; set; } = 30;

    public AssistantLimits Limits { get; set; } = new();
}
