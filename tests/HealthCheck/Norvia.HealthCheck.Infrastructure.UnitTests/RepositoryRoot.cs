namespace Norvia.HealthCheck.Infrastructure.UnitTests;

internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Found no Norvia.slnx above the test assembly.");
    }
}
