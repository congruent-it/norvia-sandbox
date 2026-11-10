using System.Text.Json.Nodes;

namespace Norvia.Integration.BookingDraft;

/// <summary>What a host answers the Shipper: the status, the JSON body, and the outcome and failure code the Message trail records. It holds no text of the request.</summary>
public sealed record BookingDraftAnswer(int StatusCode, JsonObject Body, string Outcome, string? FailureCode);
