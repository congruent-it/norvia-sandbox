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
            "docs/applications/sample.md", "docs/applications/openapi/sample.v1.json", "docs/applications/openapi/sample.v2.json");
        files[0].Content.Should().Contain("#### `Customers`");
        files[1].Content.Should().Be("{}\n");
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

        stale.Should().Equal("docs/applications/sample.md", "docs/applications/openapi/sample.v2.json");
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
}
