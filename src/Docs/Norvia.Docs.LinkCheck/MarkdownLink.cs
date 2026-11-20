namespace Norvia.Docs.LinkCheck;

/// <summary>A link or image target in a Markdown file, with the 1-based line it is written on.</summary>
public sealed record MarkdownLink(int Line, string Target);
