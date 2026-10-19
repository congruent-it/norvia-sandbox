using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The E2 Carrier invoice outcome map: turns a Canonical Model <c>CarrierInvoiceApproved</c> into the Carrier invoice outcome format the
/// Carrier Persona's invoice outcome endpoint takes. The outcome follows the Carrier invoice format: the Carrier's own <c>invoiceNo</c> and
/// <c>bookingRef</c>.
/// </summary>
/// <remarks>
/// The outcome leaves out what is Norvia's own: the Carrier ID (it is in the address), the correlation ID (it is the
/// <c>X-Correlation-ID</c> header) and each line's invoiced amount. The amounts go over as they are: the ERP decided them, and the map never
/// recalculates. A line the event calls <c>Accepted</c> is <c>ACCEPTED_ADJUSTED</c> when Finance accepted another amount than the invoiced
/// one, and a <c>Rejected</c> line is accepted at <c>0</c>.
/// </remarks>
public static class CarrierInvoiceOutcomeMap
{
    private const int InvoiceNumberLength = 50;
    private const int BookingRefLength = 200;
    private const int RemarkLength = 500;

    /// <param name="carrierInvoiceApproved">The <c>CarrierInvoiceApproved</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The outcome, or null when the text isn't a <c>CarrierInvoiceApproved</c> the Carrier can be told, so the outcome always has what its schema requires.</returns>
    public static JsonObject? ToCarrierInvoiceOutcome(string carrierInvoiceApproved)
    {
        if (JsonFields.Parse(carrierInvoiceApproved) is not { } approved
            || JsonFields.Text(approved, "eventType") != "CarrierInvoiceApproved"
            || JsonFields.Text(approved, "invoiceNumber") is not { } number
            || number.Length > InvoiceNumberLength
            || Amount(approved, "approvedAmount") is not { } approvedAmount
            || approved["lines"] is not JsonArray { Count: > 0 } sourceLines)
        {
            return null;
        }

        var lines = new JsonArray();
        foreach (var source in sourceLines)
        {
            if (Line(source as JsonObject) is not { } line)
            {
                return null;
            }

            lines.Add(line);
        }

        return new JsonObject
        {
            ["invoiceNo"] = number,
            ["approvedAmount"] = approvedAmount,
            ["lines"] = lines,
        };
    }

    private static JsonObject? Line(JsonObject? source)
    {
        if (JsonFields.Text(source, "carrierBookingReference") is not { } reference
            || reference.Length > BookingRefLength
            || JsonFields.Text(source, "result") is not { } status
            || Amount(source!, "invoicedAmount") is not { } invoicedAmount)
        {
            return null;
        }

        var remark = JsonFields.Text(source, "remark");
        if (remark is { Length: > RemarkLength })
        {
            return null;
        }

        decimal acceptedAmount;
        string result;
        switch (status)
        {
            case "Matched" when Amount(source!, "acceptedAmount") is { } matched:
                (result, acceptedAmount) = ("MATCHED", matched);
                break;
            case "Accepted" when Amount(source!, "acceptedAmount") is { } accepted:
                (result, acceptedAmount) = (accepted == invoicedAmount ? "ACCEPTED" : "ACCEPTED_ADJUSTED", accepted);
                break;
            case "Rejected":
                (result, acceptedAmount) = ("REJECTED", 0m);
                break;
            default:
                return null;
        }

        return JsonFields.WithoutNulls(new JsonObject
        {
            ["bookingRef"] = reference,
            ["result"] = result,
            ["acceptedAmount"] = acceptedAmount,
            ["remark"] = remark,
        });
    }

    private static decimal? Amount(JsonObject node, string name) =>
        node[name] is JsonValue { } value && value.TryGetValue<decimal>(out var amount) ? amount : null;
}
