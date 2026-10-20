namespace Norvia.Docs.Generator.UnitTests;

/// <summary>
/// The committed files of docs/applications match what the generator writes from the code today. An entity or an endpoint changed
/// without regenerating fails the application's test, with the command that fixes it (coding guidelines §11). They build each
/// application's EF Core model and host its API in process, so they need neither Docker nor a database.
/// </summary>
public sealed class DriftTests
{
    private const string Command = "dotnet run --project src/Docs/Norvia.Docs.Generator";

    public static TheoryData<string> Applications() =>
        [.. DocumentedApplications.All.Where(application => application.Model is not null || application.OpenApi is not null).Select(application => application.Slug)];

    [Fact]
    public void Applications_CoverTheFiveApplicationsWithADatabaseOrAnApi()
    {
        DocumentedApplications.All
            .Where(application => application.Model is not null || application.OpenApi is not null)
            .Select(application => application.Slug)
            .Should().BeEquivalentTo("tms", "erp", "partner-portal", "driver-app", "simulator");
    }

    [Theory]
    [MemberData(nameof(Applications))]
    public async Task GeneratedFiles_AreUpToDate(string slug)
    {
        var root = RepositoryRoot.Find();
        string? Read(string path) => File.Exists(Path.Combine(root, path)) ? File.ReadAllText(Path.Combine(root, path)) : null;
        var generator = new DocsGenerator([DocumentedApplications.All.Single(application => application.Slug == slug)], Read);

        var stale = generator.Stale(await generator.GenerateAsync());

        stale.Should().BeEmpty(
            $"{string.Join(", ", stale)} no longer matches the code. Regenerate and commit it with: {Command}");
    }
}
