using System.Globalization;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The E2 Carrier invoice map: turns a Carrier's invoice in the Carrier invoice format into the Canonical Model submission the ERP
/// billing API takes. The format follows the Carrier booking format of P4: its own field names, the Carrier's own booking reference as
/// <c>bookingRef</c> and local dates as dd-MM-yyyy. The Carrier is the one API Management took from the token, never one the Carrier's
/// payload names (ADR 0005), and the correlation ID is the one API Management created.
/// </summary>
/// <remarks>
/// Like the Carrier-answer map, it doesn't leave out what it can't read for the ERP to refuse: an amount it can't read as cents would
/// otherwise be rounded or dropped, and a claim changed that way is not what the Carrier sent. An invoice breaking the receiving rules for
/// its own content is reported here, as the ERP would, so the Carrier hears <c>INVALID</c> with the line at fault rather than that Norvia
/// didn't receive its invoice. The amounts go over as they are: matching them is the ERP's job.
/// </remarks>
public static class CarrierInvoiceMap
{
    private const int InvoiceNumberLength = 50;
    private const int BookingRefLength = 200;
    private const int DescriptionLength = 500;

    /// <param name="carrierInvoice">The Carrier's invoice as JSON text.</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    /// <param name="correlationId">The correlation ID API Management created for the invoice.</param>
    public static MappedCarrierInvoice ToCarrierInvoiceSubmission(string carrierInvoice, string carrierId, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var invoice = JsonFields.Parse(carrierInvoice);
        if (invoice is null)
        {
            return MappedCarrierInvoice.Invalid(null, "The invoice must be a JSON object.");
        }

        var number = JsonFields.Text(invoice, "invoiceNo");
        if (number is null || number.Length > InvoiceNumberLength)
        {
            return MappedCarrierInvoice.Invalid(null, $"The invoice needs your own invoiceNo, up to {InvoiceNumberLength} characters.");
        }

        if (Date(JsonFields.Text(invoice, "invoiceDate")) is not { } date)
        {
            return MappedCarrierInvoice.Invalid(number, "The invoice needs its invoiceDate as a date such as 19-10-2026 (dd-MM-yyyy).");
        }

        if (invoice["lines"] is not JsonArray { Count: > 0 } sourceLines)
        {
            return MappedCarrierInvoice.Invalid(number, "The invoice needs at least one line.");
        }

        var lines = new JsonArray();
        var references = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < sourceLines.Count; i++)
        {
            var (line, problem) = Line(sourceLines[i] as JsonObject, i + 1);
            if (line is null)
            {
                return MappedCarrierInvoice.Invalid(number, problem!);
            }

            if (!references.Add(line["carrierBookingReference"]!.GetValue<string>()))
            {
                return MappedCarrierInvoice.Invalid(number, $"Line {i + 1} repeats the bookingRef {line["carrierBookingReference"]}: send one line per booking.");
            }

            lines.Add(line);
        }

        return MappedCarrierInvoice.Mapped(number, new JsonObject
        {
            ["carrierId"] = carrierId,
            ["invoiceNumber"] = number,
            ["invoiceDate"] = date,
            ["correlationId"] = correlationId,
            ["lines"] = lines,
        });
    }

    private static (JsonObject? Line, string? Problem) Line(JsonObject? source, int position)
    {
        if (JsonFields.Text(source, "bookingRef") is not { } reference || reference.Length > BookingRefLength)
        {
            return (null, $"Line {position} needs the bookingRef you confirmed the booking with, up to {BookingRefLength} characters.");
        }

        if (source!["amount"] is not JsonValue value || !value.TryGetValue<decimal>(out var amount) || amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return (null, $"Line {position} needs an amount above zero, to the cent.");
        }

        var line = new JsonObject { ["carrierBookingReference"] = reference, ["amount"] = amount };
        if (source.ContainsKey("description") && source["description"] is not null)
        {
            if (JsonFields.Text(source, "description") is not { } description || description.Length > DescriptionLength)
            {
                return (null, $"The description of line {position} must be text of up to {DescriptionLength} characters.");
            }

            line["description"] = description;
        }

        return (line, null);
    }

    private static string? Date(string? date) =>
        DateOnly.TryParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
}
