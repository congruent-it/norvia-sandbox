using System.Globalization;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The E1 Shipper invoice map: turns a Canonical Model <c>ShipperInvoiceIssued</c> or <c>CreditNoteIssued</c> into the Shipper invoice
/// format (E1), which follows the Shipper booking format of P1: its own field names, the Shipper's own reference as <c>bookingRef</c> and
/// local dates as dd-MM-yyyy.
/// </summary>
/// <remarks>
/// Towards the Shipper, Norvia carries the goods, so the document names neither the Carrier nor the Shipment number (ADR 0011). It also
/// leaves out what is Norvia's own: the Shipper's name, the correlation ID and the document's status. The amounts go over as they are:
/// the ERP rated and added them up, and the map never recalculates. A Credit note carries the credited amount of its one line as a single
/// <c>FREIGHT</c> charge line: the Canonical Model's Credit note doesn't say which charge it corrects.
/// </remarks>
public static class ShipperInvoiceMap
{
    private static readonly Dictionary<string, string> ChargeCodes = new(StringComparer.Ordinal)
    {
        ["Freight"] = "FREIGHT",
        ["MinimumChargeAdjustment"] = "MINIMUM_CHARGE_ADJUSTMENT",
    };

    /// <param name="message">A <c>ShipperInvoiceIssued</c> or <c>CreditNoteIssued</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The document, or null when the text is neither an event the Shipper can be sent.</returns>
    public static JsonObject? ToShipperDocument(string message) =>
        JsonFields.Parse(message) is { } parsed && JsonFields.Text(parsed, "eventType") == "CreditNoteIssued"
            ? ToCreditNote(message)
            : ToShipperInvoice(message);

    /// <param name="shipperInvoiceIssued">The <c>ShipperInvoiceIssued</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The invoice, or null when the text isn't a <c>ShipperInvoiceIssued</c> the Shipper can be sent, so the invoice always has what its schema requires.</returns>
    public static JsonObject? ToShipperInvoice(string shipperInvoiceIssued)
    {
        if (JsonFields.Parse(shipperInvoiceIssued) is not { } issued
            || JsonFields.Text(issued, "eventType") != "ShipperInvoiceIssued"
            || issued["invoice"] is not JsonObject invoice
            || JsonFields.Text(invoice, "invoiceNumber") is not { } number
            || LocalDate(JsonFields.Text(invoice, "invoiceDate")) is not { } date
            || invoice["lines"] is not JsonArray { Count: > 0 } sourceLines
            || Amount(invoice, "totalWithoutVat") is not { } totalWithoutVat
            || Amount(invoice, "vat") is not { } vat
            || Amount(invoice, "totalWithVat") is not { } totalWithVat
            || JsonFields.Text(invoice, "currency") != "EUR")
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
            ["documentType"] = "INVOICE",
            ["documentNumber"] = number,
            ["documentDate"] = date,
            ["lines"] = lines,
            ["currency"] = "EUR",
            ["totalWithoutVat"] = totalWithoutVat,
            ["vat"] = vat,
            ["totalWithVat"] = totalWithVat,
        };
    }

    /// <param name="creditNoteIssued">The <c>CreditNoteIssued</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The Credit note, or null when the text isn't a <c>CreditNoteIssued</c> the Shipper can be sent, so the Credit note always has what its schema requires.</returns>
    public static JsonObject? ToCreditNote(string creditNoteIssued)
    {
        if (JsonFields.Parse(creditNoteIssued) is not { } issued
            || JsonFields.Text(issued, "eventType") != "CreditNoteIssued"
            || issued["creditNote"] is not JsonObject note
            || JsonFields.Text(note, "creditNoteNumber") is not { } number
            || LocalDate(JsonFields.Text(note, "creditNoteDate")) is not { } date
            || JsonFields.Text(note, "invoiceNumber") is not { } invoiceNumber
            || JsonFields.Text(note, "transportOrderNumber") is not { } transportOrderNumber
            || JsonFields.Text(note, "shipperReference") is not { } reference
            || LocalDate(JsonFields.Text(note, "deliveryDate")) is not { } deliveryDate
            || JsonFields.Text(note, "reason") is not { } reason
            || Amount(note, "totalWithoutVat") is not { } totalWithoutVat
            || Amount(note, "vat") is not { } vat
            || Amount(note, "totalWithVat") is not { } totalWithVat
            || JsonFields.Text(note, "currency") != "EUR")
        {
            return null;
        }

        return new JsonObject
        {
            ["documentType"] = "CREDIT_NOTE",
            ["documentNumber"] = number,
            ["documentDate"] = date,
            ["creditedInvoiceNumber"] = invoiceNumber,
            ["creditedTransportOrderNumber"] = transportOrderNumber,
            ["reason"] = reason,
            ["lines"] = new JsonArray(new JsonObject
            {
                ["transportOrderNumber"] = transportOrderNumber,
                ["bookingRef"] = reference,
                ["deliveryDate"] = deliveryDate,
                ["chargeLines"] = new JsonArray(new JsonObject { ["code"] = "FREIGHT", ["amount"] = totalWithoutVat }),
                ["lineTotal"] = totalWithoutVat,
            }),
            ["currency"] = "EUR",
            ["totalWithoutVat"] = totalWithoutVat,
            ["vat"] = vat,
            ["totalWithVat"] = totalWithVat,
        };
    }

    private static JsonObject? Line(JsonObject? source)
    {
        if (source is null
            || JsonFields.Text(source, "transportOrderNumber") is not { } transportOrderNumber
            || JsonFields.Text(source, "shipperReference") is not { } reference
            || LocalDate(JsonFields.Text(source, "deliveryDate")) is not { } deliveryDate
            || source["chargeLines"] is not JsonArray { Count: > 0 } sourceCharges
            || Amount(source, "total") is not { } total)
        {
            return null;
        }

        var chargeLines = new JsonArray();
        foreach (var charge in sourceCharges)
        {
            if (charge is not JsonObject chargeLine
                || JsonFields.Text(chargeLine, "type") is not { } type
                || !ChargeCodes.TryGetValue(type, out var code)
                || Amount(chargeLine, "amount") is not { } amount)
            {
                return null;
            }

            chargeLines.Add(new JsonObject { ["code"] = code, ["amount"] = amount });
        }

        return new JsonObject
        {
            ["transportOrderNumber"] = transportOrderNumber,
            ["bookingRef"] = reference,
            ["deliveryDate"] = deliveryDate,
            ["chargeLines"] = chargeLines,
            ["lineTotal"] = total,
        };
    }

    /// <summary>A date the ERP already gives as its local day, yyyy-MM-dd, as the Shipper format has it, dd-MM-yyyy.</summary>
    private static string? LocalDate(string? date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : null;

    private static decimal? Amount(JsonObject node, string name) =>
        node[name] is JsonValue { } value && value.TryGetValue<decimal>(out var amount) ? amount : null;
}
