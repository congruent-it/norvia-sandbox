using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>
/// The check after the model (ADR 0049): every claim cites a field the input holds, and every date, time, number and Transport Order number in the
/// answer is in the tracking data or the current time. A failed check drops the answer; the fixed "I have no information about that" is shown instead.
/// </summary>
public static partial class AnswerCheck
{
    public static bool Passes(AssistantReply reply, AssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(input);

        if (reply.NoInformation)
        {
            return true;
        }

        if (reply.Claims.Count == 0 || !reply.Claims.All(claim => Resolves(input.Data, claim.Field)))
        {
            return false;
        }

        var known = Tokens(input.DataText + " " + input.NowText);
        return Tokens(reply.Answer).All(known.Contains);
    }

    private static bool Resolves(JsonNode root, string path)
    {
        JsonNode? node = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PathPart().Match(part);
            if (!match.Success)
            {
                return false;
            }

            node = (node as JsonObject)?[match.Groups["name"].Value];
            if (match.Groups["index"].Success)
            {
                node = node is JsonArray array && int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture) is var i && i < array.Count ? array[i] : null;
            }

            if (node is null)
            {
                return false;
            }
        }

        return node is not null;
    }

    // Dates, times, Transport Order numbers and other numbers, each as the whole token.
    private static HashSet<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in new[] { Date(), Time(), TransportOrder(), Number() })
        {
            foreach (Match match in pattern.Matches(text))
            {
                tokens.Add(match.Value);
            }

            text = pattern.Replace(text, " ");
        }

        return tokens;
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9]*)(\[(?<index>\d+)\])?$")]
    private static partial Regex PathPart();

    [GeneratedRegex(@"(?<!\d)\d{2}-\d{2}-\d{4}(?!\d)")]
    private static partial Regex Date();

    [GeneratedRegex(@"(?<!\d)\d{1,2}:\d{2}(?!\d)")]
    private static partial Regex Time();

    [GeneratedRegex(@"\bTO-\d{7}\b", RegexOptions.IgnoreCase)]
    private static partial Regex TransportOrder();

    [GeneratedRegex(@"(?<!\d)\d+(?:[.,]\d+)?(?!\d)")]
    private static partial Regex Number();
}
