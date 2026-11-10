using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Intake agent (ADR 0041): it replays a draft for the good sample email, one with missing fields for the
/// partial one, and the refusal for two bookings. Any other text gets an empty draft that flags everything as missing: it never
/// invents a value. It makes no tool call.
/// </summary>
public static class BookingIntakeScript
{
    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        var text = BookingIntakeQuestions.TextOf(context.Question);
        return [new TextContent(text is null ? "{}" : Reply(text).ToJsonString())];
    }

    private static JsonObject Reply(string text) =>
        text.Contains("Second booking:", StringComparison.Ordinal) ? new JsonObject { ["refusal"] = "MultipleBookingsInText" }
        : text.Contains("SH-2030-0142", StringComparison.Ordinal) ? Good()
        : text.Contains("ongeveer een ton", StringComparison.Ordinal) ? Partial()
        : Nothing();

    private static JsonObject Good() => new()
    {
        ["booking"] = new JsonObject
        {
            ["bookingRef"] = "SH-2030-0142",
            ["pickupStreet"] = "Havenstraat 12",
            ["pickupPostalCode"] = "3011 AB",
            ["pickupCity"] = "Rotterdam",
            ["pickupCountry"] = "Netherlands",
            ["pickupFrom"] = "14-11-2030 08:00",
            ["pickupUntil"] = "14-11-2030 12:00",
            ["receiverName"] = "Mueller GmbH",
            ["receiverContact"] = "Anna Mueller",
            ["receiverPhone"] = "+49 221 555 0100",
            ["receiverStreet"] = "Lindenallee 5",
            ["receiverPostalCode"] = "50667",
            ["receiverCity"] = "Koeln",
            ["receiverCountry"] = "Germany",
            ["items"] = new JsonArray(new JsonObject { ["packaging"] = "PX", ["count"] = 2, ["weight"] = "850 kg", ["dimensions"] = "120x80x150 cm" }),
        },
        ["quotes"] = new JsonObject
        {
            ["bookingRef"] = "SH-2030-0142",
            ["pickupStreet"] = "Havenstraat 12",
            ["pickupPostalCode"] = "3011 AB",
            ["pickupCity"] = "Rotterdam",
            ["pickupCountry"] = "Netherlands",
            ["pickupFrom"] = "14-11-2030 between 08:00",
            ["pickupUntil"] = "14-11-2030 between 08:00 and 12:00",
            ["receiverName"] = "Mueller GmbH",
            ["receiverContact"] = "Anna Mueller",
            ["receiverPhone"] = "+49 221 555 0100",
            ["receiverStreet"] = "Lindenallee 5",
            ["receiverPostalCode"] = "50667",
            ["receiverCity"] = "Koeln",
            ["receiverCountry"] = "Germany",
            ["items.0.packaging"] = "2 pallets",
            ["items.0.count"] = "2 pallets",
            ["items.0.weight"] = "850 kg in total",
            ["items.0.dimensions"] = "120x80x150 cm",
        },
        ["missing"] = new JsonArray(),
        ["ignored"] = new JsonArray("Thanks,\nJo"),
    };

    private static JsonObject Partial() => new()
    {
        ["booking"] = new JsonObject
        {
            ["pickupCity"] = "Utrecht",
            ["receiverCity"] = "Antwerpen",
            ["items"] = new JsonArray(new JsonObject { ["packaging"] = "PX", ["count"] = 3 }),
        },
        ["quotes"] = new JsonObject
        {
            ["pickupCity"] = "Utrecht",
            ["receiverCity"] = "Antwerpen",
            ["items.0.packaging"] = "3 pallets",
            ["items.0.count"] = "3 pallets",
        },
        ["missing"] = new JsonArray(
            Missing("bookingRef", "The text gives no reference."),
            Missing("pickupStreet", "The text gives no street."),
            Missing("pickupFrom", "\"morgen\" (tomorrow) is not a date and time."),
            Missing("pickupUntil", "The text gives no pickup time."),
            Missing("receiverName", "The text does not name the receiver."),
            Missing("receiverStreet", "The text gives no delivery street."),
            Missing("items.0.weight", "\"ongeveer een ton\" is ambiguous: a ton is not a weight in a unit the booking can hold.", ambiguous: true)),
        ["ignored"] = new JsonArray("Groet,\nJan"),
    };

    private static JsonObject Nothing() => new()
    {
        ["booking"] = new JsonObject(),
        ["quotes"] = new JsonObject(),
        ["missing"] = new JsonArray(Missing("booking", "The text does not describe a booking the assistant can read.")),
    };

    private static JsonObject Missing(string field, string reason, bool ambiguous = false)
    {
        var json = new JsonObject { ["field"] = field, ["reason"] = reason };
        if (ambiguous)
        {
            json["kind"] = "ambiguous";
        }

        return json;
    }
}
