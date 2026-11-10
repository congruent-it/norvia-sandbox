using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>
/// How the host runs (ADR 0040). Off unless the estate turns the Agents on (<see cref="Enabled"/>), the same switch the other Agents have. The
/// limits repeat the stand-in's (<c>Agents</c> in LocalPartnerApi), so the operation answers the same in both places.
/// </summary>
public sealed class BookingDraftHostOptions
{
    public const string SectionName = "Agents";

    public bool Enabled { get; set; }

    /// <summary>The most characters of pasted text the host accepts. API Management caps the request first, at a size that holds this many characters of any script.</summary>
    [Range(100, 100_000)]
    public int MaxInputCharacters { get; set; } = 8_000;

    /// <summary>A short timeout and no retry: an Agent is never on a critical path.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;

    public FoundryOptions Foundry { get; set; } = new();

    public GatewayOptions Api { get; set; } = new();
}
