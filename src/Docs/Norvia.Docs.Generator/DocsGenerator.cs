namespace Norvia.Docs.Generator;

/// <summary>
/// Works out the files docs/applications should hold: each page with its generated database section filled in, and each API's OpenAPI documents.
/// <paramref name="read"/> returns a file's current text by its path relative to the repository root, or null when there is none.
/// </summary>
internal sealed class DocsGenerator(IReadOnlyList<DocumentedApplication> applications, Func<string, string?> read)
{
    public async Task<IReadOnlyList<GeneratedFile>> GenerateAsync()
    {
        var files = new List<GeneratedFile>();
        foreach (var application in applications)
        {
            if (application.Model is { } model)
            {
                var page = read(application.PagePath)
                    ?? throw new InvalidOperationException($"{application.PagePath} doesn't exist. Add the page with a <!-- generated:database --> marker.");
                var descriptions = ApplicationDescriptions.Parse(read(application.DescriptionsPath));
                files.Add(new GeneratedFile(application.PagePath, MarkedSections.Replace(page, "database", DatabaseSection.Render(model(), descriptions))));
            }

            if (application.OpenApi is { } openApi)
            {
                foreach (var (version, json) in await openApi())
                {
                    files.Add(new GeneratedFile(application.OpenApiPath(version), json));
                }
            }
        }

        return files;
    }

    /// <summary>The paths of the files whose current text differs from what <paramref name="files"/> holds.</summary>
    public IReadOnlyList<string> Stale(IEnumerable<GeneratedFile> files) =>
        [.. files.Where(file => read(file.Path) != file.Content).Select(file => file.Path)];
}
