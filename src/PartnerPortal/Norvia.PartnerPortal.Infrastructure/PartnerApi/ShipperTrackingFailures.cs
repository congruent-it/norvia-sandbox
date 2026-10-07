using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Reads what the Norvia Partner API answers to a tracking query when it isn't the answer asked for: API Management's
/// refusals and gateway errors, and the TMS's own refusals, which pass through unchanged (P8, When something fails).
/// </summary>
internal static class ShipperTrackingFailures
{
    public const string UnavailableText = "Tracking is temporarily unavailable. Try again shortly.";

    public static TrackingAnswer<T> Read<T>(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return statusCode switch
        {
            404 => TrackingAnswer.NotFound<T>(Text(json, "detail") ?? Text(json, "title") ?? "Norvia has no such Transport Order or file.", messageId),
            401 => TrackingAnswer.Failed<T>(TrackingOutcome.Refused, PartnerAnswers.UnrecognisedCredentials, messageId),
            403 => TrackingAnswer.Failed<T>(TrackingOutcome.Refused, Text(json, "text") ?? PartnerAnswers.UnrecognisedCredentials, messageId),
            429 => TrackingAnswer.Failed<T>(TrackingOutcome.Refused, "Norvia is asked for tracking too often. Try again in a moment.", messageId),
            400 or 422 => TrackingAnswer.Failed<T>(TrackingOutcome.Refused, Text(json, "detail") ?? Text(json, "title") ?? "Norvia refused the query.", messageId),
            _ => TrackingAnswer.Unavailable<T>(Text(json, "text") ?? UnavailableText, messageId),
        };
    }

    private static string? Text(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static JsonObject? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
