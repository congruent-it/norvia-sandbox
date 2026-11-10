using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Norvia.Integration.BookingDraft;

/// <summary>What reading a PDF gave: its text, or why there is none.</summary>
internal readonly record struct PdfRead(string Text, bool Unreadable = false, bool TooManyPages = false);

/// <summary>Reads the text of a PDF in .NET before the model sees it (ADR 0040). No OCR: a page with no text layer gives nothing. Nothing is kept or logged.</summary>
internal static class PdfText
{
    public static PdfRead Read(byte[] pdf, int maxPages)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            return document.NumberOfPages > maxPages
                ? new PdfRead(string.Empty, TooManyPages: true)
                : new PdfRead(string.Join('\n', document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page))));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // PdfPig throws its own and the framework's exceptions for a file that is not a PDF it can open; none carries the content.
            return new PdfRead(string.Empty, Unreadable: true);
        }
    }
}
