namespace Norvia.Docs.Generator;

/// <summary>
/// The generated part of a page sits between <c>&lt;!-- generated:name --&gt;</c> and <c>&lt;!-- /generated:name --&gt;</c>.
/// The text around the markers is written by hand and never touched.
/// </summary>
internal static class MarkedSections
{
    public static string Replace(string page, string name, string content)
    {
        var start = $"<!-- generated:{name} -->";
        var end = $"<!-- /generated:{name} -->";

        var startIndex = page.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidOperationException($"The page has no '{start}' marker.");
        }

        var endIndex = page.IndexOf(end, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new InvalidOperationException($"The page has no '{end}' marker after '{start}'.");
        }

        return $"{page[..(startIndex + start.Length)]}\n\n{content.Trim()}\n\n{page[endIndex..]}";
    }
}
