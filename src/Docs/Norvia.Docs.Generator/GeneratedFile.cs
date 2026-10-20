namespace Norvia.Docs.Generator;

/// <summary>A file the generator owns, with the content it should have. <see cref="Path"/> is relative to the repository root, with forward slashes.</summary>
internal sealed record GeneratedFile(string Path, string Content);
