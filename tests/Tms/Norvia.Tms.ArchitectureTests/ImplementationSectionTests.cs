using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Implementation section of a process doc links to the files and folders that carry each step. A link to a file that has moved
/// or gone is a lie in the code map. Only the links are checked: the Hop names in the diagram and what each row says are
/// not. A process joins when its Implementation section is written.
/// </summary>
public sealed partial class ImplementationSectionTests
{
    private static readonly string Root = RepositoryRoot.Path;

    public static TheoryData<string> Documents => new()
    {
        "docs/processes/tms/p1-transport-order-intake.md",
        "docs/processes/tms/p2-rating.md",
        "docs/processes/tms/p3-transport-planning.md",
        "docs/processes/tms/p9-freight-settlement.md",
        "docs/processes/tms/p11-trip-dispatch.md",
        "docs/processes/tms/p12-trip-execution.md",
        "docs/processes/erp/e1-invoicing.md",
        "docs/processes/erp/e2-carrier-invoice-matching.md",
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void Section_LinkedFile_Exists(string document)
    {
        var missing = Links(document).Where(link => !Exists(Path.Combine(Root, Path.GetDirectoryName(document)!, link.Split('#')[0]))).ToList();

        missing.Should().BeEmpty("every file or folder the Implementation section links to must exist, or the code map has drifted");
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void Section_LinkedFile_IsNotALineNumber(string document)
    {
        var withLines = Links(document).Where(link => link.Contains("#L", StringComparison.Ordinal)).ToList();

        withLines.Should().BeEmpty("the code map links to files and folders, never line numbers");
    }

    // The checks above pass vacuously when the section is missing or has no links.
    [Theory]
    [MemberData(nameof(Documents))]
    public void Section_Parsed_HasLinks(string document) => Links(document).Should().NotBeEmpty();

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static List<string> Links(string document)
    {
        var text = File.ReadAllText(Path.Combine(Root, document));
        var start = text.IndexOf("\n## Implementation\n", StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        var end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = text[start..(end < 0 ? text.Length : end)];

        return [.. LinkRegex().Matches(section).Select(match => match.Groups[1].Value)
            .Where(target => !target.StartsWith("http", StringComparison.Ordinal) && !target.StartsWith('#'))];
    }

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();
}
