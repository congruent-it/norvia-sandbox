namespace Norvia.Docs.LinkCheck;

/// <summary>Finds the relative links of a Markdown file whose target does not exist.</summary>
public static class DeadLinkFinder
{
    /// <param name="file">The file's path from the repository root, with forward slashes.</param>
    /// <param name="markdown">The file's text.</param>
    /// <param name="exists">Whether a path from the repository root, with forward slashes, is a file or a directory.</param>
    public static IReadOnlyList<DeadLink> Find(string file, string markdown, Func<string, bool> exists)
    {
        var directory = file.Contains('/') ? file[..file.LastIndexOf('/')] : string.Empty;
        return MarkdownLinks.Extract(markdown)
            .Where(link => Resolve(directory, link.Target) is { Length: > 0 } path && !exists(path))
            .Select(link => new DeadLink(file, link.Line, link.Target))
            .ToList();
    }

    // The path from the root a target points at: empty when it points at the file itself or the root, which always exist.
    // A fragment or query is cut off, %20 and the like decoded, and a trailing slash dropped (a directory is a target).
    private static string? Resolve(string directory, string target)
    {
        var end = target.IndexOfAny(['#', '?']);
        var path = end < 0 ? target : target[..end];
        if (path.Length == 0)
        {
            return string.Empty;
        }

        path = Uri.UnescapeDataString(path);
        var segments = path.StartsWith('/') ? new List<string>() : [.. directory.Split('/', StringSplitOptions.RemoveEmptyEntries)];
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return "../" + path; // above the root: not a path that exists
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }
}
