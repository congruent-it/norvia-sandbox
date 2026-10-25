using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Infrastructure.Estate;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Estate;

/// <summary>
/// The snapshots are the facts a local health check finds things in (#371): the fixed estate and one for each as-found preset. Each gives
/// exactly the Findings for the toggles its preset turns on, so the two cannot drift (#377).
/// </summary>
public sealed class SnapshotEstateConfigurationTests
{
    public static TheoryData<string> Snapshots => [.. SnapshotOptions.All];

    private static IReadOnlyList<Finding> FindingsOf(string snapshot) =>
        Checklist.Default.Run(SnapshotEstateConfiguration.Load(snapshot));

    private static JsonObject Preset(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "presets", $"{name}.json")))!.AsObject();

    // A toggle is on when it is true, or a list that names something.
    private static string[] TogglesOn(string preset) =>
        [.. Preset(preset).Where(toggle => toggle.Value is JsonArray { Count: > 0 } || (toggle.Value is JsonValue value && value.TryGetValue<bool>(out var on) && on)).Select(toggle => toggle.Key)];

    [Fact]
    public void TheFixedSnapshot_GivesNoFinding() =>
        FindingsOf(SnapshotOptions.Fixed).Should().BeEmpty();

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ASnapshot_GivesExactlyTheFindings_ForTheTogglesItsPresetTurnsOn(string name)
    {
        var on = TogglesOn(name);

        FindingsOf(name).Select(finding => finding.AsFoundToggle).Should().BeEquivalentTo(on);
        FindingsOf(name).Should().OnlyContain(finding => on.Contains(finding.AsFoundToggle));
    }

    [Fact]
    public void TheAsFoundSnapshot_FindsTheMissingAlertsOfTheHeadlineIncident()
    {
        var findings = FindingsOf(SnapshotOptions.AsFound);

        findings.Single(finding => finding.CheckId == "DLQ-ALERT").Evidence.Select(evidence => evidence.Subject)
            .Should().BeEquivalentTo(Preset("as-found")["entitiesWithoutDeadLetterAlert"]!.AsArray().Select(entity => entity!.GetValue<string>()))
            .And.Contain("carrier-tracking", because: "the missing alert on the tracking queue is the headline incident");
        findings.Single(finding => finding.CheckId == "FAILED-RUN-ALERT").Evidence.Select(evidence => evidence.Subject)
            .Should().BeEquivalentTo(Preset("as-found")["flowsWithoutFailedRunAlert"]!.AsArray().Select(flow => flow!.GetValue<string>()));
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ASnapshot_NamesTheFlowsWithoutTrackedProperties_AsItsPresetDoes(string name)
    {
        var listed = Preset(name)["logicAppsWithoutTrackedProperties"]!.AsArray().Select(flow => flow!.GetValue<string>());

        SnapshotEstateConfiguration.Load(name).Flows.Where(flow => flow.RecordsTrackedProperties == false).Select(flow => flow.Name).Should().BeEquivalentTo(listed);
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ASnapshots_EvidenceHoldsNoSecretValue_AndSaysWhereTheSecretIs(string name)
    {
        var evidence = FindingsOf(name).SelectMany(finding => finding.Evidence).ToList();

        evidence.Should().OnlyContain(item => EvidenceMasker.Of(item.Detail) == item.Detail && EvidenceMasker.Of(item.Subject) == item.Subject);
        if (Preset(name)["carrierSecretInlined"]!.GetValue<bool>())
        {
            evidence.Should().Contain(item => item.Subject.Contains("CARRIER_ENDPOINT_KEY", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryToggleOfThePresets_HasACheck_AndEveryCheckNamesItsToggle()
    {
        var checks = Checklist.Default.Checks;

        Preset("fixed").Select(toggle => toggle.Key).Should().OnlyContain(toggle => checks.Count(check => check.AsFoundToggle == toggle) == 1, because: "each toggle has exactly one Check that finds it");
        checks.Should().OnlyContain(check => !string.IsNullOrWhiteSpace(check.AsFoundToggle));
    }

    [Fact]
    public void TheAgentToggles_HaveTheirChecks_UntilTheEstateBuildsThem()
    {
        var presetToggles = Preset("fixed").Select(toggle => toggle.Key).ToList();

        Checklist.Default.Checks.Where(check => !presetToggles.Contains(check.AsFoundToggle)).Select(check => check.Id).Should().BeEquivalentTo(
            "AGENT-CONTENT-FILTER", "AGENT-ROLE", "AGENT-CAP", "AGENT-COST-TAG", "AGENTS-PUBLIC", "MODEL-QUOTA");
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ASnapshot_SaysWhichEntitiesLackAnAlert_AsTheSameNamedPresetDoes(string name)
    {
        var listed = Preset(name)["entitiesWithoutDeadLetterAlert"]!.AsArray().Select(entity => entity!.GetValue<string>());

        var snapshot = SnapshotEstateConfiguration.Load(name);

        snapshot.Name.Should().Be(name);
        snapshot.ServiceBusEntities.Where(entity => !entity.HasDeadLetterAlert).Select(entity => entity.Name).Should().BeEquivalentTo(listed);
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ASnapshot_HoldsEveryEntityTheEstateAlerts_AsEstateBicepListsThem(string name)
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "estate.bicep"));
        var block = bicep[(bicep.IndexOf("entities: [", StringComparison.Ordinal) + "entities: [".Length)..];
        var listed = block[..block.IndexOf(']', StringComparison.Ordinal)]
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim('\''));

        SnapshotEstateConfiguration.Load(name).ServiceBusEntities.Select(entity => entity.Name).Should().BeEquivalentTo(listed);
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ASnapshot_HoldsEveryFlowTheEstateBuilds_AsEstateBicepListsThem(string name)
    {
        var bicep = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "estate.bicep"));
        var listed = System.Text.RegularExpressions.Regex.Matches(bicep, @"\{ name: '(?<name>[^']+)', logicAppName: ").Select(match => match.Groups["name"].Value);

        SnapshotEstateConfiguration.Load(name).Flows.Select(flow => flow.Name).Should().BeEquivalentTo(listed);
    }

    [Fact]
    public void Load_ASnapshotThatDoesNotExist_Throws()
    {
        var load = () => SnapshotEstateConfiguration.Load("no-such-snapshot");

        load.Should().Throw<InvalidOperationException>().WithMessage("*no-such-snapshot*");
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
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
