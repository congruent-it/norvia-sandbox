using System.Text.RegularExpressions;
using Norvia.Tms.Application.CanonicalModel;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>
/// Checks what the Canonical Model schema fixes but JSON binding can't: required keys, formats and array items.
/// Whether a Transport Order is complete and sensible is for the Transport Order rules, not for this check.
/// </summary>
internal static partial class CanonicalShape
{
    public static Dictionary<string, string[]> Check(CanonicalTransportOrder transportOrder, string? correlationId)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            errors[TransportOrderEndpoints.CorrelationIdHeader] = ["The correlation ID header is required."];
        }

        if (string.IsNullOrWhiteSpace(transportOrder.ShipperId))
        {
            errors["shipperId"] = ["The Shipper ID is required."];
        }

        if (transportOrder.TransportOrderNumber is not null)
        {
            errors["transportOrderNumber"] = ["The TMS assigns the Transport Order number; it is never sent on submission."];
        }

        CheckCountry(transportOrder.Collection?.Address, "collection.address.country", errors);
        CheckCountry(transportOrder.Delivery?.Address, "delivery.address.country", errors);

        var goodsLines = transportOrder.GoodsLines ?? [];
        for (var i = 0; i < goodsLines.Count; i++)
        {
            if (goodsLines[i] is null)
            {
                errors[$"goodsLines[{i}]"] = ["A goods line must be an object."];
            }
        }

        return errors;
    }

    private static void CheckCountry(CanonicalAddress? address, string field, Dictionary<string, string[]> errors)
    {
        if (address?.Country is { } country && !IsoCountryCode().IsMatch(country))
        {
            errors[field] = ["The country must be an ISO 3166-1 alpha-2 code, such as NL."];
        }
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex IsoCountryCode();
}
