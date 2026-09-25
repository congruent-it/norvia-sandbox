using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>Turns a partner's dimensions, "length x width x height" text with a unit, into centimetres.</summary>
internal static partial class Dimensions
{
    public static JsonObject? ToCentimetres(string? dimensions)
    {
        var match = dimensions is null ? null : Pattern().Match(dimensions);
        if (match is not { Success: true })
        {
            return null;
        }

        var centimetresPerUnit = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "M" => 100m,
            "MM" => 0.1m,
            _ => 1m,
        };
        decimal Centimetres(string side) => PartnerNumber.Shortest(PartnerNumber.Parse(match.Groups[side].Value) * centimetresPerUnit);

        return new JsonObject
        {
            ["length"] = Centimetres("length"),
            ["width"] = Centimetres("width"),
            ["height"] = Centimetres("height"),
        };
    }

    [GeneratedRegex(
        @"^\s*(?<length>" + PartnerNumber.Pattern + @")\s*x\s*(?<width>" + PartnerNumber.Pattern + @")\s*x\s*(?<height>" + PartnerNumber.Pattern + @")\s*(?<unit>cm|mm|m)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
