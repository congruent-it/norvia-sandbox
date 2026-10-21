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
            if (application.Model is { } createModel)
            {
                var model = createModel();
                var page = read(application.PagePath)
                    ?? throw new InvalidOperationException($"{application.PagePath} doesn't exist. Add the page with a <!-- generated:database --> marker.");
                var descriptions = ApplicationDescriptions.Parse(read(application.DescriptionsPath));
                files.Add(new GeneratedFile(application.PagePath, MarkedSections.Replace(page, "database", DatabaseSection.Render(model, descriptions, application.Slug))));
                files.Add(new GeneratedFile(EntityDiagram.JsonPath(application.Slug, fromRoot: true), EntityDiagram.Json(model)));
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

    /// <summary>
    /// The ER diagrams that no longer draw the tables of their model: the Excalidraw file is missing, or draws other tables than <paramref name="files"/> holds.
    /// The generator can't draw them; <c>docs/architecture/diagrams/entities.py</c> does.
    /// </summary>
    public IReadOnlyList<string> StaleDiagrams(IEnumerable<GeneratedFile> files) =>
        [.. files.Where(file => EntityDiagram.IsModelPath(file.Path)).Where(file =>
        {
            var drawn = read(EntityDiagram.ExcalidrawPathOf(file.Path));
            return drawn is null || !EntityDiagram.DrawnTables(drawn).SetEquals(EntityDiagram.ModelledTables(file.Content));
        }).Select(file => EntityDiagram.ExcalidrawPathOf(file.Path))];
}
