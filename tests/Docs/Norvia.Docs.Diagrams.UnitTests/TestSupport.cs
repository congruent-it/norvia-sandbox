namespace Norvia.Docs.Diagrams.UnitTests;

internal static class Golden
{
    public static string Read(string name) => File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Golden", name));
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diagrams-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string Path(string name) => System.IO.Path.Combine(Directory, name);

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
