using System.Text.RegularExpressions;

namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>The one place the Shipper invoice format's codes are spelled: the name of the enum member in capitals, with underscores between the words.</summary>
internal static partial class InvoiceWireName
{
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        WordBoundary().Replace(value.ToString(), "_").ToUpperInvariant();

    public static bool TryParse<TEnum>(string? wire, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Of(candidate), wire, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
