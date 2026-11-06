using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The question put to the Simulator's personas agent, and the scripted model's reading of it. It carries only the profile, the category
/// and the language: nothing from the plan's bookings reaches the model.
/// </summary>
public static partial class PersonaQuestions
{
    public static string Ask(string profile, string style, string language, string category, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"Write {count} texts.\nprofile: {profile}\nstyle: {style}\nlanguage: {language}\ncategory: {category}");

    public static bool TryParse(string question, out string language, out string category)
    {
        ArgumentNullException.ThrowIfNull(question);

        var languageMatch = LanguageLine().Match(question);
        var categoryMatch = CategoryLine().Match(question);
        language = languageMatch.Success ? languageMatch.Groups[1].Value.Trim() : string.Empty;
        category = categoryMatch.Success ? categoryMatch.Groups[1].Value.Trim() : string.Empty;
        return languageMatch.Success && categoryMatch.Success;
    }

    [GeneratedRegex("^language: (.+)$", RegexOptions.Multiline)]
    private static partial Regex LanguageLine();

    [GeneratedRegex("^category: (.+)$", RegexOptions.Multiline)]
    private static partial Regex CategoryLine();
}
