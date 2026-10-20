using Norvia.Docs.Generator;

// Regenerates what docs/applications generates: each application's database section and OpenAPI documents (coding guidelines §11).
//   dotnet run --project src/Docs/Norvia.Docs.Generator            writes the files
//   dotnet run --project src/Docs/Norvia.Docs.Generator -- --check  writes nothing, and exits 1 when a file is stale
var root = RepositoryRoot.Find();
string? Read(string path) => File.Exists(Path.Combine(root, path)) ? File.ReadAllText(Path.Combine(root, path)) : null;

var generator = new DocsGenerator(DocumentedApplications.All, Read);
var files = await generator.GenerateAsync();
var stale = generator.Stale(files);

if (args.Contains("--check"))
{
    foreach (var path in stale)
    {
        Console.Error.WriteLine($"Stale: {path}");
    }

    if (stale.Count > 0)
    {
        Console.Error.WriteLine("Regenerate with: dotnet run --project src/Docs/Norvia.Docs.Generator");
    }

    return stale.Count == 0 ? 0 : 1;
}

foreach (var file in files.Where(file => stale.Contains(file.Path)))
{
    var path = Path.Combine(root, file.Path);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, file.Content);
    Console.WriteLine($"Wrote {file.Path}");
}

Console.WriteLine(stale.Count == 0 ? "Everything is up to date." : $"Wrote {stale.Count} file(s).");
return 0;
