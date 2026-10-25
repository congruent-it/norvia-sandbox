using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Estate;

/// <summary>
/// The snapshots are the facts a local health check finds things in (#371): the fixed estate and the estate as found. Each is checked
/// against the Findings it must give, and against the as-found preset it was derived from, so the two cannot drift (#377).
/// </summary>
public sealed class SnapshotEstateConfigurationTests
{
    private static readonly string[] AsFoundMissing =
        ["billing", "carrier-booking", "carrier-cancellation", "carrier-cost", "carrier-proof-of-delivery", "carrier-tracking", "shipper-notification"];

    private static IReadOnlyList<Finding> FindingsOf(string snapshot) =>
        Checklist.Default.Run(SnapshotEstateConfiguration.Load(snapshot));

    [Fact]
    public void TheFixedSnapshot_GivesNoFinding() =>
        FindingsOf(SnapshotOptions.Fixed).Should().BeEmpty();

    [Fact]
    public void TheAsFoundSnapshot_GivesOneFinding_ForTheMissingDeadLetterAlerts()
    {
        var finding = FindingsOf(SnapshotOptions.AsFound).Should().ContainSingle().Subject;

        finding.CheckId.Should().Be("DLQ-ALERT");
        finding.Evidence.Select(evidence => evidence.Subject).Should().Equal(AsFoundMissing);
        finding.Evidence.Should().Contain(evidence => evidence.Subject == "carrier-tracking", because: "the missing alert on the tracking queue is the headline incident");
    }

    [Theory]
    [InlineData(SnapshotOptions.Fixed)]
    [InlineData(SnapshotOptions.AsFound)]
    public void ASnapshot_SaysWhichEntitiesLackAnAlert_AsTheSameNamedPresetDoes(string name)
    {
        var preset = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "presets", $"{name}.json")))!;
        var listed = preset["entitiesWithoutDeadLetterAlert"]!.AsArray().Select(entity => entity!.GetValue<string>());

        var snapshot = SnapshotEstateConfiguration.Load(name);

        snapshot.Name.Should().Be(name);
        snapshot.ServiceBusEntities.Where(entity => !entity.HasDeadLetterAlert).Select(entity => entity.Name).Should().BeEquivalentTo(listed);
    }

    [Theory]
    [InlineData(SnapshotOptions.Fixed)]
    [InlineData(SnapshotOptions.AsFound)]
    public void ASnapshot_HoldsEveryEntityTheEstateAlerts_AsEstateBicepListsThem(string name)
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "estate.bicep"));
        var block = bicep[(bicep.IndexOf("entities: [", StringComparison.Ordinal) + "entities: [".Length)..];
        var listed = block[..block.IndexOf(']', StringComparison.Ordinal)]
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim('\''));

        SnapshotEstateConfiguration.Load(name).ServiceBusEntities.Select(entity => entity.Name).Should().BeEquivalentTo(listed);
    }

    [Fact]
    public void Load_ASnapshotThatDoesNotExist_Throws()
    {
        var load = () => SnapshotEstateConfiguration.Load("no-such-snapshot");

        load.Should().Throw<InvalidOperationException>().WithMessage("*no-such-snapshot*");
    }

    [Theory]
    [InlineData(SnapshotOptions.Fixed)]
    [InlineData(SnapshotOptions.AsFound)]
    public async Task ReadAsync_ReadsTheSnapshotTheOptionsName(string name)
    {
        var adapter = new SnapshotEstateConfiguration(Options.Create(new SnapshotOptions { Snapshot = name }));

        var estate = await adapter.ReadAsync(TestContext.Current.CancellationToken);

        estate.Name.Should().Be(name);
    }

    [Fact]
    public void TheOptions_DefaultToTheEstateAsFound() =>
        new SnapshotOptions().Snapshot.Should().Be("as-found");
}
