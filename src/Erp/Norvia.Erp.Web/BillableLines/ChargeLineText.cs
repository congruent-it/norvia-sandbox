using System.Text.RegularExpressions;

namespace Norvia.Erp.Web.BillableLines;

/// <summary>How the ERP UI words a charge line's type: <c>MinimumChargeAdjustment</c> reads "Minimum charge adjustment".</summary>
internal static partial class ChargeLineText
{
    public static string Of(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var words = WordBoundary().Replace(type, " ");
        return words.Length == 0 ? words : string.Concat(char.ToUpperInvariant(words[0]), words[1..].ToLowerInvariant());
    }

    // Between a lower-case letter or digit and a capital: MinimumChargeAdjustment, not the start of a word like "VAT".
    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
