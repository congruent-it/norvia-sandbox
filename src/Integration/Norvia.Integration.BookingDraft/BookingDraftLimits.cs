namespace Norvia.Integration.BookingDraft;

/// <summary>The most the Booking draft operation accepts (ADR 0040): characters of text, and for a PDF its bytes and its pages. The host's settings set them.</summary>
public sealed record BookingDraftLimits(int MaxInputCharacters, long MaxPdfBytes, int MaxPdfPages);
