#:project ../src/Docs/Norvia.Docs.LinkCheck/Norvia.Docs.LinkCheck.csproj

// The Markdown link check of the PR gate (coding guidelines §11): a relative link in a Markdown file that points at
// something that does not exist. No URLs, no #anchors, nothing inside code.
//
//     dotnet run infra/check-markdown-links.cs [-- <repository root>]    # prints file:line -> target per dead link; exits 1 when there is one
//
// Run it from anywhere inside the repository; the root is the nearest folder above with Norvia.slnx.

using Norvia.Docs.LinkCheck;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot(Directory.GetCurrentDirectory());
if (root is null || !Directory.Exists(root))
{
    Console.Error.WriteLine("Run it inside the repository or pass its root.");
    return 2;
}

string[] skipped = [".git", "bin", "obj", "node_modules"];
var dead = new List<DeadLink>();
var checkedFiles = 0;
foreach (var path in MarkdownFiles(root))
{
    checkedFiles++;
    var file = Path.GetRelativePath(root, path).Replace('\\', '/');
    dead.AddRange(DeadLinkFinder.Find(file, File.ReadAllText(path), Exists));
}

foreach (var link in dead)
{
    Console.WriteLine(link);
}

Console.WriteLine($"{checkedFiles} Markdown files, {dead.Count} dead links.");
return dead.Count == 0 ? 0 : 1;

IEnumerable<string> MarkdownFiles(string directory) =>
    Directory.EnumerateFiles(directory, "*.md")
        .Concat(Directory.EnumerateDirectories(directory)
            .Where(sub => !skipped.Contains(Path.GetFileName(sub)))
            .SelectMany(MarkdownFiles))
        .Order(StringComparer.Ordinal);

static string? FindRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
        {
            return directory.FullName;
        }
    }

    return null;
}

bool Exists(string target)
{
    var path = Path.Combine(root, target);
    return File.Exists(path) || Directory.Exists(path);
}
