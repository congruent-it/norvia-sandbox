using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>
/// Relays the TMS's outcome to the Shipper in the Norvia Partner API's format, unchanged in meaning
/// (contracts/partner-api/README.md, Responses).
/// </summary>
public static class BookingResponses
{
    /// <summary>The gateway error that tells the Shipper the booking was not received.</summary>
    private const int NotReceivedStatusCode = 502;

    private const string NotReceivedText = "Norvia could not take your booking in, so nothing was booked. Please send it again later.";

    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    /// <param name="correlationId">The correlation ID of the attempt, which the Shipper gets as <c>messageId</c>.</param>
    /// <param name="partnerBooking">The booking as the Shipper sent it, for its reference.</param>
    public static BookingResponse Relay(int tmsStatusCode, string tmsResponse, string correlationId, string partnerBooking)
    {
        var tms = TmsAnswer(tmsResponse);
        var answer = tmsStatusCode switch
        {
            201 => WithTransportOrderNumber(201, "ACCEPTED", tms),
            // The TMS answers a repeat with 200 rather than 201.
            200 => WithTransportOrderNumber(200, "ALREADY_BOOKED", tms),
            422 => Rejected(tms),
            _ => null,
        };

        // An answer the relay can't pass on in full would change its meaning, so the Shipper hears it wasn't received.
        answer ??= new BookingResponse(
            NotReceivedStatusCode,
            new JsonObject { ["result"] = "NOT_RECEIVED", ["text"] = NotReceivedText },
            WhyNotReceived(tmsStatusCode));
        answer.Body["messageId"] = correlationId;
        if (new PartnerFields(PartnerFields.Parse(partnerBooking)).Text("bookingRef") is { } bookingRef)
        {
            answer.Body["bookingRef"] = bookingRef;
        }

        return answer;
    }

    /// <summary>
    /// A server error, a timeout, throttling or no answer means the TMS couldn't be reached. Any other client error means
    /// the integration layer sent something the TMS won't take, which is not the Shipper's fault either.
    /// </summary>
    private static string WhyNotReceived(int tmsStatusCode) =>
        tmsStatusCode is >= 400 and < 500 and not 408 and not 429 ? "TmsCallRefused" : "TmsUnavailable";

    private static BookingResponse? WithTransportOrderNumber(int statusCode, string result, JsonNode? tms) =>
        new PartnerFields(tms).Text("transportOrderNumber") is { } number
            ? new BookingResponse(statusCode, new JsonObject { ["result"] = result, ["norviaOrderNo"] = number })
            : null;

    private static BookingResponse? Rejected(JsonNode? problem)
    {
        var errors = new JsonArray();
        foreach (var reason in (problem as JsonObject)?["reasons"] as JsonArray ?? [])
        {
            var fields = new PartnerFields(reason);
            if (fields.Text("code") is not { } code || fields.Text("message") is not { } message)
            {
                return null;
            }

            errors.Add(new JsonObject { ["code"] = code, ["text"] = message });
        }

        return errors.Count == 0 ? null : new BookingResponse(422, new JsonObject { ["result"] = "REJECTED", ["errors"] = errors });
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
