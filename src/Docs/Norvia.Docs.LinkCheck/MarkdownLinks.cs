using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Norvia.Docs.LinkCheck;

/// <summary>Finds the links of a Markdown text.</summary>
public static partial class MarkdownLinks
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static IReadOnlyList<MarkdownLink> Extract(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        return document
            .Descendants<LinkInline>()
            .Where(link => IsRelativeFileTarget(link.Url))
            .Select(link => new MarkdownLink(LineOf(markdown, link.Span.Start), link.Url!))
            .ToList();
    }

    // Not a file: empty, a pure #anchor, a network-path reference (//host) or anything with a scheme (http, https, mailto, ...).
    private static bool IsRelativeFileTarget(string? target) =>
        !string.IsNullOrEmpty(target) && !target.StartsWith('#') && !target.StartsWith("//", StringComparison.Ordinal) && !Scheme().IsMatch(target);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();

    private static int LineOf(string text, int position)
    {
        var line = 1;
        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
