using System.ComponentModel.DataAnnotations;
using Norvia.Integration.BookingDraft;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The Intake agent's guards (ADR 0040): off by default, a maximum input size, a stricter rate limit than bookings, and no retry.</summary>
public sealed class AgentsOptions : BookingDraftInputOptions
{
    public const string SectionName = "Agents";

    /// <summary>Whether the Booking draft operation does anything. The estate decides, as for the other Agents.</summary>
    public bool Enabled { get; set; }

    /// <summary>The most Booking drafts one subscription may ask for in a minute. A model call costs money, so it is far fewer than bookings.</summary>
    [Range(1, 600)]
    public int DraftsPerMinute { get; set; } = 10;

    /// <summary>A short timeout and no retry: an Agent is never on a critical path.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;
}
