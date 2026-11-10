using System.ComponentModel.DataAnnotations;
using Norvia.Integration.BookingDraft;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The Intake agent's guards (ADR 0040): off by default, a maximum input size, a stricter rate limit than bookings, and no retry.</summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    /// <summary>Whether the Booking draft operation does anything. The estate decides, as for the other Agents.</summary>
    public bool Enabled { get; set; }

    /// <summary>The most characters of pasted text the operation accepts.</summary>
    [Range(100, 100_000)]
    public int MaxInputCharacters { get; set; } = 8_000;

    /// <summary>The most bytes of an uploaded PDF the operation accepts. The PDF is read for its text and never kept.</summary>
    [Range(1_024, 10_000_000)]
    public int MaxPdfBytes { get; set; } = 1_000_000;

    /// <summary>The most pages of an uploaded PDF the operation reads.</summary>
    [Range(1, 50)]
    public int MaxPdfPages { get; set; } = 10;

    /// <summary>The limits the operation checks a request against.</summary>
    public BookingDraftLimits Limits => new(MaxInputCharacters, MaxPdfBytes, MaxPdfPages);

    /// <summary>The most Booking drafts one subscription may ask for in a minute. A model call costs money, so it is far fewer than bookings.</summary>
    [Range(1, 600)]
    public int DraftsPerMinute { get; set; } = 10;

    /// <summary>A short timeout and no retry: an Agent is never on a critical path.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;
}
