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

    /// <summary>The outcomes the Message trail records for the operation (the tracking contract's values).</summary>
    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Failed = "Failed";

    public static async Task<BookingDraftAnswer> DraftAsync(BookingDraftExtractor extractor, Stream body, int maxInputCharacters, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        var (text, code) = await ReadTextAsync(body, maxInputCharacters, cancellationToken);
        if (code is not null)
        {
            return code == InputTooLarge
                ? Refuse(correlationId, 413, code, $"The text is longer than {maxInputCharacters} characters.")
                : Refuse(correlationId, 400, code, "The request needs a text.");
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
