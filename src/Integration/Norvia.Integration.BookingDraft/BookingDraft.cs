using System.Text.Json.Nodes;

namespace Norvia.Integration.BookingDraft;

/// <summary>
/// A Booking the Intake agent filled in from a Shipper's text: not a Booking until the Shipper submits it. The booking part is in the
/// partner booking format (contracts/partner-api/booking.v1.schema.json); each value in it has the quote it came from.
/// </summary>
public sealed record BookingDraft(JsonObject Booking, IReadOnlyDictionary<string, string> Quotes, IReadOnlyList<MissingField> Missing)
{
    /// <summary>The draft as the Partner API's <c>booking-draft</c> document.</summary>
    public JsonObject ToJson() => new()
    {
        ["booking"] = Booking.DeepClone(),
        ["quotes"] = new JsonObject(Quotes.Select(quote => KeyValuePair.Create(quote.Key, (JsonNode?)JsonValue.Create(quote.Value)))),
        ["missing"] = new JsonArray([.. Missing.Select(field => (JsonNode)new JsonObject { ["field"] = field.Field, ["reason"] = field.Reason })]),
    };
}
