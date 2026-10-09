using System.Globalization;
using System.Text.RegularExpressions;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>The Carrier cost in words a Carrier reads: "412.50 EUR" and "Minimum charge adjustment 12.50".</summary>
public static partial class CarrierCostText
{
    public static string Total(CarrierCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return $"{Amount(cost.Total)} {cost.Currency}";
    }

    public static string Line(CarrierChargeLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var words = CamelHumps().Replace(line.Type, " $1").Trim().ToLowerInvariant();
        return words.Length == 0
            ? Amount(line.Amount)
            : $"{char.ToUpperInvariant(words[0])}{words[1..]} {Amount(line.Amount)}";
    }

    private static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex CamelHumps();
}
