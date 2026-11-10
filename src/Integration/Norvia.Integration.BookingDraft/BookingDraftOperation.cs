using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Integration.BookingDraft;

/// <summary>
/// The Booking draft operation once the edge has let a request through (ADR 0040): read the text, ask the Intake agent, answer with the draft or a
/// coded refusal. API Management's policy in Azure and the Partner API stand-in locally do the edge; both hosts answer with this, so the status, the
/// code and the body are the same. The text is read once and never kept or logged.
/// </summary>
public static class BookingDraftOperation
{
    public const string AgentsDisabled = "AgentsDisabled";
    public const string InputTooLarge = "InputTooLarge";
    public const string TextRequired = "TextRequired";
    private const string PdfTooLarge = "PdfTooLarge";
    public const string UnsupportedFormat = "UnsupportedFormat";
    public const string NoTextInPdf = "NoTextInPdf";

    /// <summary>The outcomes the Message trail records for the operation (the tracking contract's values).</summary>
    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Failed = "Failed";

    public static async Task<BookingDraftAnswer> DraftAsync(BookingDraftExtractor extractor, Stream body, string? contentType, BookingDraftLimits limits, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        var (text, code) = await ReadAsync(body, contentType, limits, cancellationToken);
        if (code is not null)
        {
            return code switch
            {
                InputTooLarge => Refuse(correlationId, 413, code, $"The text is longer than {limits.MaxInputCharacters} characters."),
                PdfTooLarge => Refuse(correlationId, 413, InputTooLarge, $"The PDF is over {limits.MaxPdfPages} pages or {limits.MaxPdfBytes / 1024} KB."),
                UnsupportedFormat => Refuse(correlationId, 415, code, "Send the text or a PDF."),
                NoTextInPdf => Refuse(correlationId, 422, code, "This PDF has no text to read; paste the text instead."),
                _ => Refuse(correlationId, 400, code, "The request needs a text."),
            };
        }

        var outcome = await extractor.ExtractAsync(text!, correlationId, cancellationToken);
        if (outcome.Draft is { } draft)
        {
            return new BookingDraftAnswer(200, draft.ToJson(), Succeeded, null);
        }

        return outcome.FailureCode == BookingDraftFailureCodes.MultipleBookingsInText
            ? Refuse(correlationId, 422, outcome.FailureCode, "The text describes more than one booking. Paste one at a time.")
            : Fail(correlationId, outcome.FailureCode ?? BookingDraftFailureCodes.AgentUnavailable);
    }

    /// <summary>A refusal with its status and the stable code (docs/architecture/failure-behaviour.md).</summary>
    public static BookingDraftAnswer Refuse(string correlationId, int statusCode, string failureCode, string text) =>
        new(statusCode, ErrorBody(correlationId, failureCode, text), Refused, failureCode);

    /// <summary>The assistant failed: the Shipper is told it is unavailable and nothing else changes.</summary>
    public static BookingDraftAnswer Fail(string correlationId, string failureCode) =>
        new(503, ErrorBody(correlationId, failureCode, "The assistant is unavailable."), Failed, failureCode);

    private static JsonObject ErrorBody(string correlationId, string failureCode, string text) =>
        new() { ["messageId"] = correlationId, ["failureCode"] = failureCode, ["text"] = text };

    /// <summary>A body is text as JSON (<c>{"text": "..."}</c>, the original form) or as <c>text/plain</c>, or a PDF; anything else is refused. A missing content type is JSON.</summary>
    private static async Task<(string? Value, string? Code)> ReadAsync(Stream body, string? contentType, BookingDraftLimits limits, CancellationToken cancellationToken)
    {
        var mediaType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType.Split(';')[0].Trim().ToLowerInvariant();
        switch (mediaType)
        {
            case "application/json":
                return await ReadTextAsync(body, limits.MaxInputCharacters, cancellationToken);
            case "text/plain":
                using (var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true))
                {
                    var plain = await reader.ReadToEndAsync(cancellationToken);
                    return string.IsNullOrWhiteSpace(plain) ? (null, TextRequired)
                        : plain.Length > limits.MaxInputCharacters ? (null, InputTooLarge) : (plain, null);
                }

            case "application/pdf":
                return await ReadPdfAsync(body, limits, cancellationToken);
            default:
                return (null, UnsupportedFormat);
        }
    }

    private static async Task<(string? Value, string? Code)> ReadPdfAsync(Stream body, BookingDraftLimits limits, CancellationToken cancellationToken)
    {
        // One byte more than the cap is enough to know it is over, without reading a whole oversize upload.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            if (buffer.Length > limits.MaxPdfBytes)
            {
                return (null, PdfTooLarge);
            }
        }

        var read = PdfText.Read(buffer.ToArray(), limits.MaxPdfPages);
        return read switch
        {
            { Unreadable: true } => (null, UnsupportedFormat),
            { TooManyPages: true } => (null, PdfTooLarge),
            { Text: var text } when string.IsNullOrWhiteSpace(text) => (null, NoTextInPdf),
            { Text: var text } when text.Length > limits.MaxInputCharacters => (null, InputTooLarge),
            _ => (read.Text, null),
        };
    }

    private static async Task<(string? Value, string? Code)> ReadTextAsync(Stream body, int maxCharacters, CancellationToken cancellationToken)
    {
        JsonNode? request;
        try
        {
            request = await JsonNode.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return (null, TextRequired);
        }

        if (request is not JsonObject { } document || document["text"] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
        {
            return (null, TextRequired);
        }

        return text.Length > maxCharacters ? (null, InputTooLarge) : (text, null);
    }
}
