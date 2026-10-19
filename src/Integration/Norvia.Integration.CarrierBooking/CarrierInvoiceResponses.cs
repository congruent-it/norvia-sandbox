using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// Relays the ERP billing API's response to a Carrier's invoice to the Carrier, in the Carrier invoice response format and unchanged in
/// meaning (contracts/partner-api/README.md, Carrier invoice): each line's result straight away, and the ERP's failure codes as stable
/// results with readable messages.
/// </summary>
public static class CarrierInvoiceResponses
{
    private const string NotReceivedText = "Norvia could not take your invoice in, so nothing was recorded. Please send it again later.";
    private const string CarrierNotFoundText = "Norvia has no Carrier that matches your credentials, so nothing was recorded.";

    private static readonly Dictionary<string, string> LineResults = new(StringComparer.Ordinal)
    {
        ["Matched"] = "MATCHED",
        ["PriceMismatch"] = "PRICE_MISMATCH",
        ["UnknownBooking"] = "UNKNOWN_BOOKING",
        ["Duplicate"] = "DUPLICATE",
    };

    /// <param name="mapped">What the map made of the Carrier's invoice.</param>
    /// <param name="erpStatusCode">The status the ERP answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <param name="erpResponse">The ERP's answer as JSON text, or empty when it didn't answer.</param>
    /// <param name="correlationId">The correlation ID of the invoice, which the Carrier gets as <c>messageId</c>.</param>
    public static BookingResponse Relay(MappedCarrierInvoice mapped, int erpStatusCode, string erpResponse, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        var number = mapped.InvoiceNumber;
        if (mapped.Problem is { } problem)
        {
            return Refusal(400, "INVALID", number, problem, correlationId, "CarrierInvoiceUnreadable");
        }

        var answer = CarrierAnswerResponses.TmsAnswer(erpResponse);
        return erpStatusCode switch
        {
            200 or 201 when Received(mapped, answer, erpStatusCode, correlationId) is { } received => received,
            400 => Refusal(400, "INVALID", number, ProblemText(answer, "Norvia could not use your invoice."), correlationId, ProblemCode(answer) ?? "ErpCallRefused"),
            409 => Refusal(409, "INVOICE_NUMBER_USED", number, $"You already sent an invoice with number {number}, with different content. Send what you claim now with a new invoice number.", correlationId, null),
            422 => Refusal(422, "CARRIER_NOT_FOUND", number, CarrierNotFoundText, correlationId, null),
            _ => Refusal(502, "NOT_RECEIVED", number, NotReceivedText, correlationId, ErpInvoiceOutcome.WhyNotReceived(erpStatusCode)),
        };
    }

    // An answer the relay can't pass on in full would change its meaning, so the Carrier hears it wasn't received.
    private static BookingResponse? Received(MappedCarrierInvoice mapped, JsonNode? erp, int erpStatusCode, string correlationId)
    {
        if ((erp as JsonObject)?["invoice"] is not JsonObject invoice
            || Text(invoice, "invoiceNumber") != mapped.InvoiceNumber
            || Text(invoice, "status") is not { } status
            || status is not ("Received" or "Approved")
            || invoice["lines"] is not JsonArray { Count: > 0 } sourceLines
            || !Amount(invoice, "approvedAmount", out var approvedAmount))
        {
            return null;
        }

        var lines = new JsonArray();
        foreach (var source in sourceLines)
        {
            if (source is not JsonObject line
                || Text(line, "carrierBookingReference") is not { } reference
                || Text(line, "result") is not { } result
                || !LineResults.TryGetValue(result, out var lineResult)
                || line["amount"] is not JsonValue amount
                || !amount.TryGetValue<decimal>(out var claimed))
            {
                return null;
            }

            lines.Add(new JsonObject { ["bookingRef"] = reference, ["amount"] = claimed, ["result"] = lineResult });
        }

        var repeat = (erp as JsonObject)?["repeat"] is JsonValue value && value.TryGetValue<bool>(out var repeated) && repeated;
        return new BookingResponse(erpStatusCode, new JsonObject
        {
            ["result"] = "RECEIVED",
            ["messageId"] = correlationId,
            ["invoiceNo"] = mapped.InvoiceNumber,
            ["repeated"] = repeat,
            ["status"] = status.ToUpperInvariant(),
            ["approvedAmount"] = approvedAmount,
            ["lines"] = lines,
        });
    }

    private static BookingResponse Refusal(int statusCode, string result, string? number, string text, string correlationId, string? failureCode)
    {
        var body = new JsonObject { ["result"] = result, ["messageId"] = correlationId };
        if (number is not null)
        {
            body["invoiceNo"] = number;
        }

        body["text"] = text;
        return new BookingResponse(statusCode, body, failureCode);
    }

    private static string ProblemText(JsonNode? problem, string fallback) =>
        Text(problem as JsonObject, "detail") ?? Text(problem as JsonObject, "title") ?? fallback;

    private static string? ProblemCode(JsonNode? problem) => Text(problem as JsonObject, "code");

    private static string? Text(JsonObject? node, string name) => JsonFields.Text(node, name);

    // The approved amount is null until the invoice is Approved; a value of another kind can't be passed on.
    private static bool Amount(JsonObject node, string name, out decimal? amount)
    {
        amount = null;
        if (node[name] is null)
        {
            return true;
        }

        if (node[name] is JsonValue value && value.TryGetValue<decimal>(out var number))
        {
            amount = number;
            return true;
        }

        return false;
    }
}
