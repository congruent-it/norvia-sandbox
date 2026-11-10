using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Norvia.Integration.BookingDraft;

/// <summary>How reading a PDF ended.</summary>
internal enum PdfReadStatus
{
    /// <summary>The PDF was opened and its text read (the text may be empty: a scan).</summary>
    Read,

    /// <summary>The file is not a PDF that can be opened.</summary>
    Unreadable,

    /// <summary>The PDF has more pages than the cap, so none was read.</summary>
    TooManyPages,
}

/// <summary>What reading a PDF gave: its text, or why there is none.</summary>
internal readonly record struct PdfRead(PdfReadStatus Status, string Text = "");

/// <summary>Reads the text of a PDF in .NET before the model sees it (ADR 0040). No OCR: a page with no text layer gives nothing. Nothing is kept or logged.</summary>
internal static class PdfText
{
    public static PdfRead Read(byte[] pdf, int maxPages)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            return document.NumberOfPages > maxPages
                ? new PdfRead(PdfReadStatus.TooManyPages)
                : new PdfRead(PdfReadStatus.Read, string.Join('\n', document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // PdfPig throws its own and the framework's exceptions for a file that is not a PDF it can open; none carries the content.
            return new PdfRead(PdfReadStatus.Unreadable);
        }
    }
}
