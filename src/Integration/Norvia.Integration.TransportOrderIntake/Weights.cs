using System.Text.RegularExpressions;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>Turns a partner's weight, text with a unit, into kilograms as a number.</summary>
internal static partial class Weights
{
    private const decimal KilogramsPerPound = 0.45359237m;

    /// <summary>Weights are kept to the gram, which is finer than any scale a Shipper weighs goods on.</summary>
    private const int Decimals = 3;

    public static decimal? ToKilograms(string? weight)
    {
        var match = weight is null ? null : Pattern().Match(weight);
        if (match is not { Success: true })
        {
            return null;
        }

        var amount = PartnerNumber.Parse(match.Groups["amount"].Value);
        var kilograms = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "T" => amount * 1000m,
            "LB" or "LBS" => amount * KilogramsPerPound,
            _ => amount,
        };
        return PartnerNumber.Shortest(Math.Round(kilograms, Decimals, MidpointRounding.AwayFromZero));
    }

    [GeneratedRegex(@"^\s*(?<amount>" + PartnerNumber.Pattern + @")\s*(?<unit>kg|t|lbs?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
