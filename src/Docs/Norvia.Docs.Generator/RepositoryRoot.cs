using System.Diagnostics.CodeAnalysis;

namespace Norvia.Docs.Generator;

/// <summary>The repository's root directory, found from the running assembly.</summary>
[ExcludeFromCodeCoverage(Justification = "Reads the file system.")]
internal static class RepositoryRoot
{
    public static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Found no Norvia.slnx above the generator.");
    }
}
