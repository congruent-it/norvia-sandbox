using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The whole checklist against every snapshot, through the app's agent API as the Agent reads it (#401): a Check fires for its toggle and only
/// its toggle, the fixed estate gives no Finding, every toggle of the as-found estate has a Check, and no Finding carries a secret's value.
/// The toggles each snapshot turns on are read from <c>infra/presets</c>, the Bicep's own input, so the snapshots cannot drift from them
/// unseen. Each test starts the app and the stand-in of its own.
/// </summary>
public sealed class ChecklistAgainstSnapshotsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The snapshot of each preset, which has the same name.
    public static TheoryData<string> Snapshots => new() { "as-found", "fixed", "contrast", "look-alike", "silent-carrier" };

    [Theory]
    [MemberData(nameof(Snapshots))]
    public async Task ASnapshot_GivesExactlyTheFindingsForTheTogglesItsPresetTurnsOn(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        var view = await RunChecksAsync(host);

        view["estate"]!.GetValue<string>().Should().Be(snapshot);
        view["checksRun"]!.GetValue<int>().Should().Be(Checklist.Default.Checks.Count, "a missing Finding must mean the Check passed, not that it did not run");
        // The Findings that pair a failure with a missing alert (#395) come from the failures a snapshot holds and share the alert Checks' toggles.
        var findings = ToggleFindings(view);
        findings.Select(finding => finding.AsFoundToggle).Should().BeEquivalentTo(TogglesOn(snapshot), "a Check fires for its toggle and only its toggle");
        findings.Select(finding => finding.AsFoundToggle).Should().OnlyHaveUniqueItems("a toggle gives one Finding, however many resources it names");
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public async Task EachFinding_IsItsChecks_AndNamesTheResourcesOfItsToggleAlone(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        var findings = ToggleFindings(await RunChecksAsync(host));

        foreach (var finding in findings)
        {
            var check = Checklist.Default.Checks.Should().ContainSingle(check => check.Id == finding.CheckId).Subject;
            check.AsFoundToggle.Should().Be(finding.AsFoundToggle);
            finding.Evidence.Should().NotBeEmpty("a Finding names what it found");
            finding.Fix.Should().Contain(finding.AsFoundToggle, "the fix is to put the toggle back");

            // A toggle that lists resources is as long as its list in the preset, and the Finding names each.
            if (Preset(snapshot)[finding.AsFoundToggle] is JsonArray listed)
            {
                var names = listed.Select(item => item!.GetValue<string>());
                finding.Evidence.Select(evidence => evidence.Subject).Should().Contain(names);
            }
        }
    }

    [Fact]
    public async Task TheFixedSnapshot_GivesNoFinding()
    {
        await using var host = new HealthCheckAgentHost("fixed");

        Findings(await RunChecksAsync(host)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLookAlikeSnapshot_DiffersFromTheFixedEstateByTheCacheAlone_AndTheContrastSnapshotByNothing()
    {
        await using var lookAlike = new HealthCheckAgentHost("look-alike");
        await using var contrast = new HealthCheckAgentHost("contrast");

        Findings(await RunChecksAsync(lookAlike)).Select(finding => finding.AsFoundToggle).Should().Equal("shipperTrackingCached");
        Findings(await RunChecksAsync(contrast)).Should().BeEmpty();
    }

    [Fact]
    public void EveryToggleOfTheAsFoundEstate_HasACheck_AndEveryCheckNamesAToggleOfItsOwn()
    {
        var checks = Checklist.Default.Checks.Where(check => check is not UntoldFailureCheck).ToList();

        // The presets are the toggles of #377; the Checks with a toggle outside them are the agent toggles of #369.
        var presetToggles = Preset("as-found").Select(toggle => toggle.Key).ToList();
        presetToggles.Should().NotBeEmpty();
        checks.Select(check => check.AsFoundToggle).Should().Contain(presetToggles, "every toggle of the as-found estate has a Check that finds it");
        checks.Select(check => check.AsFoundToggle).Should().OnlyHaveUniqueItems("a Check maps to exactly one toggle");
        foreach (var name in Directory.GetFiles(PresetsDirectory, "*.json").Select(Path.GetFileNameWithoutExtension))
        {
            Preset(name!).Select(toggle => toggle.Key).Should().BeEquivalentTo(presetToggles, $"the {name} preset sets every toggle");
        }
    }

    [Fact]
    public async Task TheInlinedCarrierSecretFinding_NamesWhereTheSecretIs_AndNotWhatItIs()
    {
        await using var host = new HealthCheckAgentHost("as-found");

        var finding = Findings(await RunChecksAsync(host)).Single(finding => finding.AsFoundToggle == "carrierSecretInlined");

        finding.Severity.Should().Be(nameof(Severity.Critical));
        finding.Evidence.Select(evidence => evidence.Subject).Should().Contain("booking Logic App setting CARRIER_ENDPOINT_KEY");
        finding.Evidence.Select(evidence => evidence.Subject + evidence.Detail).Should().NotContain(text => text.Contains("CARRIER_ENDPOINT_KEY=", StringComparison.Ordinal) || text.Contains("CARRIER_ENDPOINT_KEY:", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public async Task NoFinding_OfASnapshot_CarriesASecretValue(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        var findings = Findings(await RunChecksAsync(host));

        // Masking is idempotent, so whatever it would still change is a value that got through.
        foreach (var text in findings.SelectMany(finding => finding.Evidence.SelectMany(evidence => new[] { evidence.Subject, evidence.Detail })))
        {
            EvidenceMasker.Of(text).Should().Be(text, "no secret value is left in evidence");
            text.Should().NotMatchRegex(@"[A-Za-z0-9+/=_-]{32,}", "nothing in evidence looks like a key or a token");
        }
    }

    private sealed record FindingRow(string CheckId, string AsFoundToggle, string Severity, string Fix, IReadOnlyList<EvidenceRow> Evidence);

    private sealed record EvidenceRow(string Subject, string Detail);

    private static string PresetsDirectory { get; } = Path.Combine(RepositoryRoot(), "infra", "presets");

    private static JsonObject Preset(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(PresetsDirectory, $"{name}.json")))!.AsObject();

    /// <summary>The toggles a preset turns on: a flag that is set, or a list of resources that is not empty.</summary>
    private static string[] TogglesOn(string name) =>
    [
        .. Preset(name).Where(toggle => toggle.Value switch
        {
            JsonArray list => list.Count > 0,
            JsonValue value => value.GetValue<bool>(),
            _ => false,
        }).Select(toggle => toggle.Key),
    ];

    // The Findings that pair a failure with a missing alert (#395) come from the failures a snapshot holds and name only the resources that failed,
    // so a toggle's list does not describe them. Their own cases are in UntoldFailuresAgainstSnapshotsTests (#404).
    private static List<FindingRow> ToggleFindings(JsonNode view) =>
        [.. Findings(view).Where(finding => finding.CheckId is not (UntoldFailedRunsCheck.CheckId or UntoldDeadLettersCheck.CheckId))];

    private static List<FindingRow> Findings(JsonNode view) =>
    [
        .. view["findings"]!.AsArray().Select(finding => new FindingRow(
            finding!["checkId"]!.GetValue<string>(),
            finding["asFoundToggle"]!.GetValue<string>(),
            finding["severity"]!.GetValue<string>(),
            finding["fix"]!.GetValue<string>(),
            [.. finding["evidence"]!.AsArray().Select(evidence => new EvidenceRow(evidence!["subject"]!.GetValue<string>(), evidence["detail"]!.GetValue<string>()))])),
    ];

    /// <summary>Calls <c>run_checks</c> through the stand-in as an Agent does and returns the agent view's JSON.</summary>
    private static async Task<JsonNode> RunChecksAsync(HealthCheckAgentHost host)
    {
        using var client = host.StandInClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/mcp/health-check", UriKind.Relative),
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = "run_checks", ["arguments"] = new JsonObject() },
            },
            Ct);
        response.EnsureSuccessStatusCode();
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse();
        return JsonNode.Parse(reply["content"]![0]!["text"]!.GetValue<string>())!;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "infra", "presets")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root, with infra/presets, is not above the test's output.");
    }
}
