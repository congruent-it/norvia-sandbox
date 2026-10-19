using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// A Carrier invoice in the Carrier invoice format of the Norvia Partner API
/// (contracts/partner-api/carrier-invoice.v1.schema.json): its own invoice number, a local date as dd-MM-yyyy and, per
/// line, the Carrier's own booking reference. It never names the Carrier or a correlation ID: Norvia takes them from the
/// token and from API Management (ADR 0005).
/// </summary>
internal static class CarrierInvoiceFormat
{
    public static JsonObject ToJson(string invoiceNumber, DateOnly invoiceDate, IReadOnlyList<CarrierInvoiceLineDraft> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new JsonObject
        {
            ["invoiceNo"] = invoiceNumber,
            ["invoiceDate"] = invoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            ["lines"] = new JsonArray([.. lines.Select(LineOf)]),
        };
    }

    private static JsonNode LineOf(CarrierInvoiceLineDraft line)
    {
        var json = new JsonObject { ["bookingRef"] = line.BookingRef.Trim(), ["amount"] = line.Amount };
        if (!string.IsNullOrWhiteSpace(line.Description))
        {
            json["description"] = line.Description.Trim();
        }

        return json;
    }
}
