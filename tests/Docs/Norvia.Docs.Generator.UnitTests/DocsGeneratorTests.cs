namespace Norvia.Docs.Generator.UnitTests;

public sealed class DocsGeneratorTests
{
    private const string Page = "# Sample\n\n<!-- generated:database -->\n<!-- /generated:database -->\n";

    private static readonly DocumentedApplication Sample = new(
        "sample",
        SampleDatabase.Model,
        () => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["v1"] = "{}\n", ["v2"] = "{ }\n" }));

    private static DocsGenerator GeneratorOver(Dictionary<string, string> files, params DocumentedApplication[] applications) =>
        new(applications, path => files.GetValueOrDefault(path));

    [Fact]
    public async Task GenerateAsync_AnApplicationWithADatabaseAndAnApi_GivesItsPageAndOneDocumentPerVersion()
    {
        var generator = GeneratorOver(new() { ["docs/applications/sample.md"] = Page }, Sample);

        var files = await generator.GenerateAsync();

        files.Select(file => file.Path).Should().Equal(
            "docs/applications/sample.md", "docs/architecture/diagrams/er-sample.json",
            "docs/applications/openapi/sample.v1.json", "docs/applications/openapi/sample.v2.json");
        files[0].Content.Should().Contain("#### `Customers`");
        files[2].Content.Should().Be("{}\n");
    }

    [Fact]
    public async Task GenerateAsync_Descriptions_AreUsedInThePage()
    {
        var generator = GeneratorOver(
            new()
            {
                ["docs/applications/sample.md"] = Page,
                ["docs/applications/descriptions/sample.json"] = """{ "tables": { "Customers": { "description": "Who orders." } } }""",
            },
            Sample);

        var files = await generator.GenerateAsync();

        files[0].Content.Should().Contain("Who orders.");
    }

    [Fact]
    public async Task GenerateAsync_AnApplicationWithNothingToGenerate_GivesNoFiles()
    {
        var generator = GeneratorOver([], new DocumentedApplication("console"));

        (await generator.GenerateAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_ADatabaseWithoutAPage_Throws()
    {
        var generate = () => GeneratorOver([], Sample).GenerateAsync();

        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*docs/applications/sample.md*");
    }

    [Fact]
    public async Task Stale_FilesThatDifferFromTheGeneratedOnes_AreListed()
    {
        var files = new Dictionary<string, string> { ["docs/applications/sample.md"] = Page, ["docs/applications/openapi/sample.v1.json"] = "{}\n" };
        var generator = GeneratorOver(files, Sample);

        var stale = generator.Stale(await generator.GenerateAsync());

        stale.Should().Equal("docs/applications/sample.md", "docs/architecture/diagrams/er-sample.json", "docs/applications/openapi/sample.v2.json");
    }

    [Fact]
    public async Task Stale_FilesThatAreUpToDate_ListsNothing()
    {
        var files = new Dictionary<string, string> { ["docs/applications/sample.md"] = Page };
        var generator = GeneratorOver(files, Sample);
        foreach (var file in await generator.GenerateAsync())
        {
            files[file.Path] = file.Content;
        }

        generator.Stale(await generator.GenerateAsync()).Should().BeEmpty();
    }

    // An Excalidraw file as entities.py writes it: each table is a title, then a subtitle that starts with its key columns.
    private static string DiagramOf(params string[] tables) =>
        "{\"elements\":[" + string.Join(",", tables.SelectMany(table => new[] { table, "PK Id (int)" }).Select(text => $"{{\"type\":\"text\",\"text\":\"{text}\"}}")) + "]}";

    private static async Task<(DocsGenerator Generator, IReadOnlyList<GeneratedFile> Files)> GenerateWith(string? diagram)
    {
        var files = new Dictionary<string, string> { ["docs/applications/sample.md"] = Page };
        if (diagram is not null)
        {
            files["docs/architecture/diagrams/er-sample.excalidraw"] = diagram;
        }

        var generator = GeneratorOver(files, Sample);
        return (generator, await generator.GenerateAsync());
    }

    [Fact]
    public async Task StaleDiagrams_ADiagramThatDrawsTheModelsTables_ListsNothing()
    {
        var (generator, files) = await GenerateWith(DiagramOf("Customers", "Orders", "OrderNotes", "Receipts"));

        generator.StaleDiagrams(files).Should().BeEmpty();
    }

    [Fact]
    public async Task StaleDiagrams_ADiagramThatMissesATable_IsListed()
    {
        var (generator, files) = await GenerateWith(DiagramOf("Customers", "Orders", "OrderNotes"));

        generator.StaleDiagrams(files).Should().Equal("docs/architecture/diagrams/er-sample.excalidraw");
    }

    [Fact]
    public async Task StaleDiagrams_ADiagramThatDrawsATableThatIsGone_IsListed()
    {
        var (generator, files) = await GenerateWith(DiagramOf("Customers", "Orders", "OrderNotes", "Receipts", "Invoices"));

        generator.StaleDiagrams(files).Should().Equal("docs/architecture/diagrams/er-sample.excalidraw");
    }

    [Fact]
    public async Task StaleDiagrams_NoDiagramFile_IsListed()
    {
        var (generator, files) = await GenerateWith(null);

        generator.StaleDiagrams(files).Should().Equal("docs/architecture/diagrams/er-sample.excalidraw");
    }
}
