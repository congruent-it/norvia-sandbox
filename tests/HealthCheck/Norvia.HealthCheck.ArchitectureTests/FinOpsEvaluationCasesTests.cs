using System.Text.Json.Nodes;
using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.ArchitectureTests;

/// <summary>
/// The FinOps agent's evaluation set (#513, evaluation/cases/finops), seeded from the <c>chattyTelemetry</c> incident. The cases run on
/// demand (docs/operations/agent-evaluation.md), never here; these only keep the set in step with the rising-spend Check and the agent.
/// </summary>
public sealed class FinOpsEvaluationCasesTests
{
    private const string FindingId = "F-" + RisingSpendCheck.CheckId;

    private static Dictionary<string, JsonNode> Cases() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "evaluation", "cases", "finops"), "*.json")
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!)
            .ToDictionary(c => c["id"]!.GetValue<string>());

    private static IEnumerable<string> Strings(JsonNode? list) => list?.AsArray().Select(item => item!.GetValue<string>()) ?? [];

    private static JsonNode FindingIds(JsonNode c) => c["expect"]!["lists"]!["findings[].id"]!;

    [Fact]
    public void EveryCase_IsForTheFinOpsAgent_AndReadsBothCostTools()
    {
        Cases().Should().NotBeEmpty();
        foreach (var (id, c) in Cases())
        {
            c["agent"]!.GetValue<string>().Should().Be("finops", id);
            Strings(c["expect"]!["toolsCalled"]).Should().BeEquivalentTo(["get_cost_view", "get_rising_spend_findings"], id);
            Strings(c["expect"]!["toolsNotCalled"]).Should().BeEmpty(id);
        }
    }

    [Fact]
    public void TheIncident_ExpectsExactlyTheRisingSpendFinding_AndNamesItsToggle()
    {
        var cases = Cases();

        cases.Should().ContainKey("chatty-telemetry-names-the-workspace-and-the-toggle");
        var incident = cases["chatty-telemetry-names-the-workspace-and-the-toggle"];
        Strings(FindingIds(incident)["exactly"]).Should().Equal([FindingId]);
        incident["setup"]!["toggle"]!.GetValue<string>().Should().Contain(new RisingSpendCheck().AsFoundToggle);
        incident["expect"]!["explanation"]!["rubric"]!.GetValue<string>().Should().Contain(new RisingSpendCheck().AsFoundToggle);
    }

    [Fact]
    public void TheCasesWithoutARiseAtFlatVolume_ExpectNoFinding_AndAnExplanation()
    {
        var cases = Cases();

        foreach (var id in new[] { "fixed-estate-no-rising-spend", "spend-rises-with-volume-is-no-finding" })
        {
            cases.Should().ContainKey(id);
            Strings(FindingIds(cases[id])["exactly"]).Should().BeEmpty(id);
            cases[id]["expect"]!["explanation"]!["rubric"].Should().NotBeNull(id);
        }
    }

    [Fact]
    public void TheCasesThatPushForANumberOrAnotherCause_AddNoFinding()
    {
        var cases = Cases();

        foreach (var id in new[] { "asks-for-the-monthly-saving", "asks-whether-another-resource-drives-the-bill" })
        {
            cases.Should().ContainKey(id);
            Strings(FindingIds(cases[id])["exactly"]).Should().Equal([FindingId], id);
        }
    }

    [Fact]
    public void TheInjectionCases_KeepTheFinding_ForbidTheirOwnText_AndSayHowTheyAreSeeded()
    {
        var injections = Cases().Where(c => c.Key.StartsWith("injection-", StringComparison.Ordinal)).ToList();

        injections.Select(c => c.Key).Should().BeEquivalentTo(["injection-in-a-resource-name", "injection-in-a-resource-name-asks-for-a-figure", "injection-in-a-tag"]);
        foreach (var (id, c) in injections)
        {
            Strings(FindingIds(c)["exactly"]).Should().Equal([FindingId], id);
            Strings(c["expect"]!["mustNotContain"]).Should().NotBeEmpty(id);
            c["setup"]!["seed"]!.GetValue<string>().Should().NotBeEmpty(id);
        }
    }
}
