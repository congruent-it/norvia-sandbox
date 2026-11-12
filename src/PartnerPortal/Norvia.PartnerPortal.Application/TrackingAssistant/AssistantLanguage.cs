using System.Text.RegularExpressions;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The language of a question, for the fixed texts the portal shows itself. Dutch when the question is clearly Dutch, English otherwise (ADR 0049).</summary>
public static partial class AssistantLanguage
{
    private static readonly HashSet<string> Dutch = new(StringComparer.OrdinalIgnoreCase)
    {
        "waar", "mijn", "mijn", "zending", "bestelling", "wanneer", "wordt", "geleverd", "is", "het", "de", "een", "ik", "wat", "hoe", "niet", "nog", "komt", "aan", "vrachtwagen", "levering", "ophalen", "opgehaald", "onderweg", "ontvangen", "ingepland", "afgeleverd", "hoelaat", "alstublieft", "dank", "bedankt",
    };

    private static readonly HashSet<string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        "where", "my", "order", "shipment", "when", "will", "be", "delivered", "is", "the", "a", "i", "what", "how", "not", "yet", "arrive", "truck", "delivery", "pickup", "collected", "transit", "received", "scheduled", "please", "thanks",
    };

    public static AssistantLanguageKind Detect(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var words = Word().Matches(question).Select(match => match.Value).ToList();
        var dutch = words.Count(Dutch.Contains);
        var english = words.Count(English.Contains);
        return dutch > english ? AssistantLanguageKind.Dutch : AssistantLanguageKind.English;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}

public enum AssistantLanguageKind
{
    English,
    Dutch,
}
