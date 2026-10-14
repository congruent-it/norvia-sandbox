using System.Text.RegularExpressions;

namespace Norvia.Simulator.Web.UiTests;

/// <summary>What a log line's Message trail link looks like: its accessible name, and its address in the Integration console, with the correlation ID captured as <c>key</c>.</summary>
public static partial class TrailLinks
{
    [GeneratedRegex(@"/\?key=(?<key>[0-9a-fA-F-]{32,})&within=all$")]
    public static partial Regex ConsoleSearch();

    [GeneratedRegex("Open the Message trail of .* in the Integration console")]
    public static partial Regex Name();
}
