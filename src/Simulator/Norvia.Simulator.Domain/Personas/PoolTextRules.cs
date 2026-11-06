using System.Text.RegularExpressions;

namespace Norvia.Simulator.Domain.Personas;

/// <summary>
/// What a generated text must be before the plan keeps it: short, one line, and free of anything that identifies a person or a place. A
/// text that fails is dropped. The model is told nothing of the bookings, so this is a second guard rather than the first.
/// </summary>
public static partial class PoolTextRules
{
    public const int MaxLength = 120;

    /// <summary>A pool with fewer texts than this left after validation is not used: the built-in pool is.</summary>
    public const int MinPoolSize = 3;

    public static bool IsAcceptable(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Length <= MaxLength
        && !text.Any(char.IsControl)
        && !Url().IsMatch(text)
        && !Email().IsMatch(text)
        && !Phone().IsMatch(text)
        && !Postcode().IsMatch(text)
        && !Street().IsMatch(text);

    /// <summary>The texts that pass, trimmed, each once and in the order given.</summary>
    public static IReadOnlyList<string> Acceptable(IEnumerable<string?> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return [.. texts.Where(IsAcceptable).Select(text => text!.Trim()).Distinct(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"https?:|www\.|\b[\p{L}0-9-]+\.(com|nl|de|be|org|net|eu|io|co|info)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"\S@\S")]
    private static partial Regex Email();

    // Seven or more digits, which may be spread over spaces, dashes, dots and brackets.
    [GeneratedRegex(@"\+?\d[\d\s\-().]{5,}\d")]
    private static partial Regex Phone();

    // 3011 AB (Dutch), 40213 (German) or 2000 (Belgian) after a word such as postcode.
    [GeneratedRegex(@"\b\d{4}\s?[A-Za-z]{2}\b|\b\d{5}\b|\b(postcode|plz|zip)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Postcode();

    // "Hoofdstraat 12", "Main Street 4", "12 Baker Road".
    [GeneratedRegex(@"\b\p{L}*(straat|laan|weg|plein|kade|gracht|strasse|straße|allee|street|road|avenue|lane|drive)\s+\d+|\b\d+\s+\p{L}+\s+(street|road|avenue|lane|drive)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Street();
}
