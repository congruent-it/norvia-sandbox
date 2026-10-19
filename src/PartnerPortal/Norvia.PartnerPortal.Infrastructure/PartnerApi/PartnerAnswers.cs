using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Reads Norvia's answer to a booking (contracts/partner-api/README.md, Responses, and API Management's refusals). An
/// answer that doesn't say a Transport Order exists, or why the booking was rejected, means the booking wasn't received:
/// nothing was booked, and sending it again is safe.
/// </summary>
internal static class PartnerAnswers
{
    public const string UnrecognisedCredentials = "Norvia didn't recognise this Persona's subscription key or token.";

    public static NorviaAnswer Read(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return (statusCode, Text(json, "result")) switch
        {
            (201, "ACCEPTED") when Text(json, "norviaOrderNo") is { } number => NorviaAnswer.Accepted(number, messageId),
            (200, "ALREADY_BOOKED") when Text(json, "norviaOrderNo") is { } number => NorviaAnswer.AlreadyBooked(number, messageId),
            (422, "REJECTED") when Reasons(json) is { Count: > 0 } reasons => NorviaAnswer.Rejected(reasons, messageId),
            (403, _) => NorviaAnswer.Refused(Text(json, "text") ?? UnrecognisedCredentials, messageId),
            (401, _) => NorviaAnswer.Refused(UnrecognisedCredentials, messageId),
            _ => NorviaAnswer.NotReceived(messageId),
        };
    }

    /// <summary>
    /// Reads Norvia's reply to a Carrier's answer (contracts/partner-api/README.md, Carrier answer, and API Management's
    /// refusals). A reply that doesn't say the answer was recorded, or why not, means it wasn't received: nothing was
    /// recorded, and answering again is safe.
    /// </summary>
    public static NorviaReply ReadReply(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return (statusCode, Text(json, "result")) switch
        {
            (200, "RECORDED") => NorviaReply.Recorded(messageId, json?["repeated"] is JsonValue repeated && repeated.TryGetValue<bool>(out var value) && value),
            (409, "BOOKING_NOT_OPEN") => Refusal(CarrierAnswerResult.BookingNotOpen, json, messageId, "Norvia no longer has this booking open."),
            (422, "REFERENCE_IN_USE") => Refusal(CarrierAnswerResult.ReferenceInUse, json, messageId, "Norvia has this reference on another confirmed booking of yours."),
            (404, "BOOKING_NOT_FOUND") => Refusal(CarrierAnswerResult.BookingNotFound, json, messageId, "Norvia has no such booking."),
            (403, "NOT_ALLOWED") => Refusal(CarrierAnswerResult.NotAllowed, json, messageId, "This booking is not yours to answer."),
            (400, "INVALID") => Refusal(CarrierAnswerResult.Invalid, json, messageId, "Norvia can't use this answer."),
            (403, _) => NorviaReply.Refusal(CarrierAnswerResult.Refused, Text(json, "text") ?? UnrecognisedCredentials, messageId),
            (401, _) => NorviaReply.Refusal(CarrierAnswerResult.Refused, UnrecognisedCredentials, messageId),
            _ => NorviaReply.NotReceived(messageId),
        };
    }

    /// <summary>
    /// Reads API Management's answer to a Tracking event (contracts/partner-api/README.md, Carrier tracking): 202 Accepted
    /// and nothing more. Anything else that isn't a refusal of the Persona's credentials means it wasn't received.
    /// </summary>
    public static TrackingReply ReadTrackingReply(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return statusCode switch
        {
            202 => TrackingReply.Accepted(messageId),
            403 => TrackingReply.Refused(Text(json, "text") ?? UnrecognisedCredentials, messageId),
            401 => TrackingReply.Refused(UnrecognisedCredentials, messageId),
            _ => TrackingReply.NotReceived(messageId),
        };
    }

    /// <summary>
    /// Reads API Management's answer to a Proof of delivery (contracts/partner-api/README.md, Carrier Proof of delivery):
    /// 202 Accepted and nothing more. A body over the limit is refused, in Norvia's words; anything else that isn't a refusal
    /// of the Persona's credentials means it wasn't received.
    /// </summary>
    public static TrackingReply ReadProofOfDeliveryReply(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return statusCode switch
        {
            413 => TrackingReply.Refused(Text(json, "text") ?? "A Proof of delivery is at most 25 MB.", messageId),
            _ => ReadTrackingReply(statusCode, body, correlationId),
        };
    }

    /// <summary>
    /// Reads Norvia's reply to a Carrier invoice (contracts/partner-api/README.md, Carrier invoice responses, and API
    /// Management's refusals). A reply that doesn't say the invoice was received, or why not, means it wasn't: nothing was
    /// recorded, and sending it again is safe.
    /// </summary>
    public static CarrierInvoiceReply ReadCarrierInvoiceReply(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return (statusCode, Text(json, "result")) switch
        {
            (200 or 201, "RECEIVED") when Receipt(json) is { } receipt => CarrierInvoiceReply.Received(messageId, receipt),
            (409, "INVOICE_NUMBER_USED") => InvoiceRefusal(CarrierInvoiceReplyResult.InvoiceNumberUsed, json, messageId, "Norvia has this invoice number with different content."),
            (422, "CARRIER_NOT_FOUND") => InvoiceRefusal(CarrierInvoiceReplyResult.CarrierNotFound, json, messageId, "Norvia has no Carrier for this Persona."),
            (400, "INVALID") => InvoiceRefusal(CarrierInvoiceReplyResult.Invalid, json, messageId, "Norvia can't use this invoice."),
            (403, _) => CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.Refused, Text(json, "text") ?? UnrecognisedCredentials, messageId),
            (401, _) => CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.Refused, UnrecognisedCredentials, messageId),
            _ => CarrierInvoiceReply.NotReceived(messageId),
        };
    }

    private static CarrierInvoiceReply InvoiceRefusal(CarrierInvoiceReplyResult result, JsonObject? json, string? messageId, string fallbackText) =>
        CarrierInvoiceReply.Refusal(result, Text(json, "text") ?? fallbackText, messageId);

    // A received invoice with a line the portal can't read isn't relayed in part: it counts as not received, as the Logic App does.
    private static CarrierInvoiceReceipt? Receipt(JsonObject? json)
    {
        if (json?["repeated"] is not JsonValue repeated || !repeated.TryGetValue<bool>(out var wasRepeated)
            || Text(json, "status") is not { } status
            || json["lines"] is not JsonArray { Count: > 0 } wireLines)
        {
            return null;
        }

        var approved = status switch
        {
            "APPROVED" => CarrierInvoiceStatus.Approved,
            "RECEIVED" => CarrierInvoiceStatus.Received,
            _ => (CarrierInvoiceStatus?)null,
        };
        if (approved is null)
        {
            return null;
        }

        var lines = new List<CarrierInvoiceReceiptLine>();
        foreach (var node in wireLines)
        {
            if (node is not JsonObject line
                || Text(line, "bookingRef") is not { } bookingRef
                || line["amount"] is not JsonValue amount || !amount.TryGetValue<decimal>(out var lineAmount)
                || !CarrierInvoiceLineResults.TryParse(Text(line, "result"), out var result))
            {
                return null;
            }

            lines.Add(new CarrierInvoiceReceiptLine(bookingRef, lineAmount, result));
        }

        decimal? approvedAmount = json["approvedAmount"] is JsonValue value && value.TryGetValue<decimal>(out var total) ? total : null;
        return new CarrierInvoiceReceipt(wasRepeated, approved.Value, approvedAmount, lines);
    }

    private static NorviaReply Refusal(CarrierAnswerResult result, JsonObject? json, string? messageId, string fallbackText) =>
        NorviaReply.Refusal(result, Text(json, "text") ?? fallbackText, messageId);

    private static List<RejectionReason>? Reasons(JsonObject? json)
    {
        var reasons = new List<RejectionReason>();
        foreach (var error in json?["errors"] as JsonArray ?? [])
        {
            if (Text(error as JsonObject, "code") is not { } code || Text(error as JsonObject, "text") is not { } text)
            {
                return null;
            }

            reasons.Add(new RejectionReason(code, text));
        }

        return reasons;
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
