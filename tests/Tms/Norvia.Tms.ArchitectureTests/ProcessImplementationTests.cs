using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Implementation section of the P1 process doc (docs/processes/tms/p1-transport-order-intake.md) links to the files
/// that carry each step. A link to a file that has moved or gone is a lie in the code map.
/// </summary>
public sealed partial class ProcessImplementationTests
{
    private static readonly string Root = FindRepositoryRoot();
    private const string Document = "docs/processes/tms/p1-transport-order-intake.md";

    [Fact]
    public void Section_LinkedFile_Exists()
    {
        var missing = Links().Where(link => !File.Exists(Path.Combine(Root, Path.GetDirectoryName(Document)!, link.Split('#')[0]))).ToList();

        missing.Should().BeEmpty("every file the Implementation section links to must exist, or the code map has drifted");
    }

    [Fact]
    public void Section_LinkedFile_IsNotALineNumber()
    {
        var withLines = Links().Where(link => link.Contains("#L", StringComparison.Ordinal)).ToList();

        withLines.Should().BeEmpty("the code map links to files, never line numbers");
    }

    // The checks above pass vacuously when the section is missing or has no links.
    [Fact]
    public void Section_Parsed_HasLinks() => Links().Should().NotBeEmpty();

    private static List<string> Links()
    {
        var text = File.ReadAllText(Path.Combine(Root, Document));
        var start = text.IndexOf("## Implementation", StringComparison.Ordinal);
        var end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = start < 0 ? string.Empty : text[start..(end < 0 ? text.Length : end)];

        return [.. LinkRegex().Matches(section).Select(match => match.Groups[1].Value)
            .Where(target => !target.StartsWith("http", StringComparison.Ordinal) && !target.StartsWith('#'))];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Found no Norvia.slnx above the test assembly.");
    }

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();
}
