using System.Text.RegularExpressions;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>How the API writes a date and a time (<c>dd-MM-yyyy HH:mm</c>), found in a question or in an answer.</summary>
internal static partial class TrackingPatterns
{
    [GeneratedRegex(@"(?<!\d)\d{2}-\d{2}-\d{4}(?!\d)")]
    public static partial Regex Date();

    [GeneratedRegex(@"(?<!\d)\d{1,2}:\d{2}(?!\d)")]
    public static partial Regex Time();
}
