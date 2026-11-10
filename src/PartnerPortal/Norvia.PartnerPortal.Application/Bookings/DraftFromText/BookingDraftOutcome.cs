namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>How Norvia's Intake agent answered a Shipper's pasted text.</summary>
public enum BookingDraftOutcome
{
    /// <summary>A Booking draft came back.</summary>
    Drafted,

    /// <summary>The text describes more than one booking; paste one at a time.</summary>
    MultipleBookings,

    /// <summary>The PDF has no text to read (a scan or an image); paste the text instead. There is no OCR.</summary>
    NoTextInPdf,

    /// <summary>The file is not a text or a PDF the assistant can read.</summary>
    UnsupportedFormat,

    /// <summary>The text, or the PDF, is larger than the assistant accepts.</summary>
    TooLarge,

    /// <summary>The assistant is off, refused the call, is down, or gave a reply that was not a draft. Nothing is drafted.</summary>
    Unavailable,
}
