using System.Text;
using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// Relays the TMS's response to a Carrier's answer to the Carrier, in the Carrier answer response format and unchanged
/// in meaning (contracts/partner-api/README.md, Carrier answer).
/// </summary>
public static class CarrierAnswerResponses
{
    private const string NotReceivedText = "Norvia could not take your answer in, so nothing was recorded. Please send it again later.";
    private const string NotOpenText = "Norvia no longer has this booking open: it was withdrawn, or already has a different answer.";

    /// <param name="mapped">What the map made of the Carrier's answer.</param>
    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    /// <param name="correlationId">The correlation ID of the answer, which the Carrier gets as <c>messageId</c>.</param>
    public static BookingResponse Relay(MappedCarrierAnswer mapped, int tmsStatusCode, string tmsResponse, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        var number = mapped.CarrierBookingNumber;
        if (mapped.Problem is { } problem)
        {
            return Refusal(400, "INVALID", number, problem, correlationId, "CarrierAnswerUnreadable");
        }

        return tmsStatusCode switch
        {
            200 when Recorded(mapped, TmsAnswer(tmsResponse), correlationId) is { } recorded => recorded,
            403 => Refusal(403, "NOT_ALLOWED", number, "This booking is not yours to answer.", correlationId, null),
            404 => Refusal(404, "BOOKING_NOT_FOUND", number, $"Norvia has no booking {number}.", correlationId, null),
            409 => Refusal(409, "BOOKING_NOT_OPEN", number, NotOpenText, correlationId, null),
            _ => Refusal(502, "NOT_RECEIVED", number, NotReceivedText, correlationId, TmsAnswerOutcome.WhyNotReceived(tmsStatusCode)),
        };
    }

    // An answer the relay can't pass on in full would change its meaning, so the Carrier hears it wasn't received.
    private static BookingResponse? Recorded(MappedCarrierAnswer mapped, JsonNode? tms, string correlationId)
    {
        var fields = new PartnerFields(tms);
        var decision = fields.Text("status") switch
        {
            "Confirmed" => "CONFIRMED",
            "Rejected" => "DECLINED",
            _ => null,
        };
        if (decision is null || fields.Text("carrierBookingNumber") is not { } number || number != mapped.CarrierBookingNumber)
        {
            return null;
        }

        return new BookingResponse(200, new JsonObject
        {
            ["result"] = "RECORDED",
            ["messageId"] = correlationId,
            ["carrierBookingNumber"] = number,
            ["decision"] = decision,
            ["repeated"] = (tms as JsonObject)?["repeat"] is JsonValue repeat && repeat.TryGetValue<bool>(out var value) && value,
        });
    }

    private static BookingResponse Refusal(int statusCode, string result, string? number, string text, string correlationId, string? failureCode)
    {
        var body = new JsonObject { ["result"] = result, ["messageId"] = correlationId };
        if (number is not null)
        {
            body["carrierBookingNumber"] = number;
        }

        body["text"] = text;
        return new BookingResponse(statusCode, body, failureCode);
    }

    /// <summary>
    /// The TMS's answer. Logic Apps passes a body whose content type it doesn't read as JSON, such as the TMS's
    /// <c>application/problem+json</c>, as base64 <c>$content</c>.
    /// </summary>
    private static JsonNode? TmsAnswer(string tmsResponse)
    {
        var answer = PartnerFields.Parse(tmsResponse);
        if (new PartnerFields(answer).Text("$content") is not { } content)
        {
            return answer;
        }

        try
        {
            return PartnerFields.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(content)));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
