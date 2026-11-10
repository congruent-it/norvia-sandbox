using System.ComponentModel.DataAnnotations;
using Norvia.Integration.BookingDraft;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>
/// How the host runs (ADR 0040). Off unless the estate turns the Agents on (<see cref="Enabled"/>), the same switch the other Agents have. The
/// limits repeat the stand-in's (<c>Agents</c> in LocalPartnerApi), so the operation answers the same in both places.
/// </summary>
public sealed class BookingDraftHostOptions : BookingDraftInputOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }

    /// <summary>A short timeout and no retry: an Agent is never on a critical path.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;

    public FoundryOptions Foundry { get; set; } = new();

    public GatewayOptions Api { get; set; } = new();
}
