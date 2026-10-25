using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The failures that nobody was told about, through the app's agent API as the Agent reads it (#404): the failed runs and dead-letter counts the
/// snapshots hold, listed with whether an alert watches them, and the Findings that pair each with its missing alert. No snapshot holds a failure
/// that an alert watches, so those cases give the failed flow and queue their alert on the facts the snapshot reads. Each test starts the app and
/// the stand-in of its own.
/// </summary>
public sealed class UntoldFailuresAgainstSnapshotsTests
{
    private const string FailedFlow = "carrier-tracking";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> SnapshotsWithTheTrackingFailures => new() { "as-found", "silent-carrier" };

    [Theory]
    [MemberData(nameof(SnapshotsWithTheTrackingFailures))]
    public async Task ListFailedRuns_OnASnapshotWithTheFailures_NamesTheFlowThatFailed_WithNoAlertWatchingIt(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        var view = await CallToolAsync(host, "list_failed_runs");

        view["estate"]!.GetValue<string>().Should().Be(snapshot);
        view["windowDays"]!.GetValue<int>().Should().Be(EstateConfiguration.FailureWindowDays);
        var flow = view["flows"]!.AsArray().Should().ContainSingle("only the Logic Apps that failed are listed").Subject!;
        flow["flow"]!.GetValue<string>().Should().Be(FailedFlow);
        flow["failedRuns"]!.GetValue<int>().Should().Be(14);
        flow["hasFailedRunAlert"]!.GetValue<bool>().Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(SnapshotsWithTheTrackingFailures))]
    public async Task ListDeadLetterQueues_OnASnapshotWithTheFailures_NamesTheQueueThatHoldsMessages_WithNoAlertWatchingIt(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        var view = await CallToolAsync(host, "list_dead_letter_queues");

        view["estate"]!.GetValue<string>().Should().Be(snapshot);
        var queue = view["queues"]!.AsArray().Should().ContainSingle("only the queues that hold messages are listed").Subject!;
        queue["name"]!.GetValue<string>().Should().Be(FailedFlow);
        queue["deadLetteredMessages"]!.GetValue<int>().Should().Be(3);
        queue["hasDeadLetterAlert"]!.GetValue<bool>().Should().BeFalse();
        queue.AsObject().Select(property => property.Key).Should().BeEquivalentTo(["name", "deadLetteredMessages", "hasDeadLetterAlert"], "the list carries no message body");
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("contrast")]
    [InlineData("look-alike")]
    public async Task TheLists_OnASnapshotWithNoFailure_AreEmpty_AndNoUntoldFindingIsRaised(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        (await CallToolAsync(host, "list_failed_runs"))["flows"]!.AsArray().Should().BeEmpty();
        (await CallToolAsync(host, "list_dead_letter_queues"))["queues"]!.AsArray().Should().BeEmpty();
        UntoldFindings(await CallToolAsync(host, "run_checks")).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(SnapshotsWithTheTrackingFailures))]
    public async Task RunChecks_OnASnapshotWithFailuresAndNoAlerts_PairsEachFailureWithItsMissingAlert(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);

        var findings = UntoldFindings(await CallToolAsync(host, "run_checks"));

        findings.Select(finding => finding["checkId"]!.GetValue<string>()).Should().BeEquivalentTo(UntoldFailedRunsCheck.CheckId, UntoldDeadLettersCheck.CheckId);
        foreach (var finding in findings)
        {
            finding["severity"]!.GetValue<string>().Should().Be(nameof(Severity.Critical));
            var evidence = finding["evidence"]!.AsArray().Should().ContainSingle("only the flow or queue that failed is named").Subject!;
            evidence["subject"]!.GetValue<string>().Should().Be(FailedFlow);
        }

        Detail(findings, UntoldFailedRunsCheck.CheckId).Should().Contain("14 times").And.Contain("no failed-run alert");
        Detail(findings, UntoldDeadLettersCheck.CheckId).Should().Contain("3 messages").And.Contain("no alert");
    }

    [Fact]
    public async Task WhenAnAlertWatchesTheFailures_TheListsShowIt_AndNoUntoldFindingIsRaised()
    {
        await using var host = new HealthCheckAgentHost("as-found", adjustEstate: WithAlertsOn(FailedFlow));

        var flow = (await CallToolAsync(host, "list_failed_runs"))["flows"]!.AsArray().Should().ContainSingle().Subject!;
        var queue = (await CallToolAsync(host, "list_dead_letter_queues"))["queues"]!.AsArray().Should().ContainSingle().Subject!;
        var findings = UntoldFindings(await CallToolAsync(host, "run_checks"));

        flow["failedRuns"]!.GetValue<int>().Should().Be(14, "the failures are listed whether or not they were told about");
        flow["hasFailedRunAlert"]!.GetValue<bool>().Should().BeTrue();
        queue["deadLetteredMessages"]!.GetValue<int>().Should().Be(3);
        queue["hasDeadLetterAlert"]!.GetValue<bool>().Should().BeTrue();
        findings.Should().BeEmpty("somebody is told");
    }

    [Fact]
    public async Task WhenOnlyOneOfTwoFailuresIsWatched_TheFindingNamesTheOneThatIsNot()
    {
        await using var host = new HealthCheckAgentHost("as-found", adjustEstate: estate =>
        {
            var watched = WithAlertsOn("billing")(estate);
            return watched with
            {
                Flows = [.. watched.Flows.Select(flow => flow.Name == "billing" ? flow with { FailedRuns = 2 } : flow)],
                ServiceBusEntities = [.. watched.ServiceBusEntities.Select(entity => entity.Name == "billing" ? entity with { DeadLetteredMessages = 1 } : entity)],
            };
        });

        var failedRuns = await CallToolAsync(host, "list_failed_runs");
        var findings = UntoldFindings(await CallToolAsync(host, "run_checks"));

        failedRuns["flows"]!.AsArray().Select(flow => flow!["flow"]!.GetValue<string>()).Should().Equal([FailedFlow, "billing"], "the most failed first");
        findings.Should().HaveCount(2);
        foreach (var finding in findings)
        {
            finding["evidence"]!.AsArray().Select(evidence => evidence!["subject"]!.GetValue<string>()).Should().Equal([FailedFlow]);
        }
    }

    [Fact]
    public async Task AFailureWhoseCountCouldNotBeRead_IsNotListed_AndGivesNoFinding()
    {
        await using var host = new HealthCheckAgentHost("as-found", adjustEstate: estate => estate with
        {
            Flows = [.. estate.Flows.Select(flow => flow with { FailedRuns = null })],
            ServiceBusEntities = [.. estate.ServiceBusEntities.Select(entity => entity with { DeadLetteredMessages = null })],
        });

        (await CallToolAsync(host, "list_failed_runs"))["flows"]!.AsArray().Should().BeEmpty();
        (await CallToolAsync(host, "list_dead_letter_queues"))["queues"]!.AsArray().Should().BeEmpty();
        UntoldFindings(await CallToolAsync(host, "run_checks")).Should().BeEmpty("a Check gives no Finding for what it could not see");
    }

    private static Func<EstateConfiguration, EstateConfiguration> WithAlertsOn(string name) => estate => estate with
    {
        Flows = [.. estate.Flows.Select(flow => flow.Name == name ? flow with { HasFailedRunAlert = true } : flow)],
        ServiceBusEntities = [.. estate.ServiceBusEntities.Select(entity => entity.Name == name ? entity with { HasDeadLetterAlert = true } : entity)],
    };

    private static List<JsonNode> UntoldFindings(JsonNode view) =>
    [
        .. view["findings"]!.AsArray().Select(finding => finding!)
            .Where(finding => finding["checkId"]!.GetValue<string>() is UntoldFailedRunsCheck.CheckId or UntoldDeadLettersCheck.CheckId),
    ];

    private static string Detail(List<JsonNode> findings, string checkId) =>
        findings.Single(finding => finding["checkId"]!.GetValue<string>() == checkId)["evidence"]![0]!["detail"]!.GetValue<string>();

    /// <summary>Calls a tool through the stand-in as an Agent does and returns the agent view's JSON.</summary>
    private static async Task<JsonNode> CallToolAsync(HealthCheckAgentHost host, string tool)
    {
        using var client = host.StandInClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/mcp/health-check", UriKind.Relative),
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() },
            },
            Ct);
        response.EnsureSuccessStatusCode();
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse();
        return JsonNode.Parse(reply["content"]![0]!["text"]!.GetValue<string>())!;
    }
}
