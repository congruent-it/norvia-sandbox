using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.BookingDraft;

/// <summary>How much the Booking draft operation accepts (ADR 0040): the settings every host binds from <c>Agents</c>, so the stand-in and the Azure host answer alike.</summary>
public abstract class BookingDraftInputOptions
{
    /// <summary>The most characters of pasted text the operation accepts, and of text read from a PDF.</summary>
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
}
