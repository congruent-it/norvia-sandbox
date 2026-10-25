using System.Text.Json.Nodes;
using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.ArchitectureTests;

/// <summary>
/// The Estate health-check agent's evaluation set (#396, evaluation/cases/estate-health-check): every toggle has a case whose known
/// answer is its Check's Finding, and the no-false-positive, never-invents and injection cases exist. The cases run on demand
/// (docs/operations/agent-evaluation.md), never here; these only keep the set in step with the checklist.
/// </summary>
public sealed class EvaluationCasesTests
{
    private static Dictionary<string, JsonNode> Cases() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "evaluation", "cases", "estate-health-check"), "*.json")
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!)
            .ToDictionary(c => c["id"]!.GetValue<string>());

    private static IEnumerable<string> Strings(JsonNode? list) => list?.AsArray().Select(item => item!.GetValue<string>()) ?? [];

    [Fact]
    public void EveryToggle_HasACase_WhoseKnownAnswerIsItsChecksFinding()
    {
        var cases = Cases().Values.Where(c => c["id"]!.GetValue<string>().StartsWith("toggle-", StringComparison.Ordinal)).ToList();
        var toggleChecks = Checklist.Default.Checks.Where(check => check is not UntoldFailureCheck).ToList();

        cases.Should().HaveCount(toggleChecks.Count);
        foreach (var check in toggleChecks)
        {
            var match = cases.Where(c => c["setup"]!["toggle"]!.GetValue<string>().StartsWith(check.AsFoundToggle + ",", StringComparison.Ordinal)).ToList();
            var found = match.Should().ContainSingle(check.AsFoundToggle).Subject;
            var expected = found["expect"]!["lists"]!["findings[].id"]!;
            Strings((expected["exactly"] ?? expected["includes"])).Should().Equal([$"F-{check.Id}"], check.AsFoundToggle);
            found["expect"]!["explanation"]!["rubric"]!.GetValue<string>().Should().Contain(check.Title, check.AsFoundToggle);
            Strings(found["expect"]!["toolsCalled"]).Should().Contain("run_checks");
        }
    }

    [Fact]
    public void TheCasesWithoutAProblem_ExpectAnEmptyListAndSayNothingWasFound()
    {
        var cases = Cases();

        foreach (var id in new[] { "fixed-estate-no-findings", "asks-about-a-problem-no-check-found", "asks-about-a-finding-that-is-gone" })
        {
            cases.Should().ContainKey(id);
            Strings(cases[id]["expect"]!["lists"]!["findings[].id"]!["exactly"]).Should().BeEmpty(id);
            cases[id]["expect"]!["explanation"].Should().NotBeNull(id);
        }
    }

    [Fact]
    public void TheInjectionCases_KeepTheFindingAndForbidTheirOwnText()
    {
        var injections = Cases().Where(c => c.Key.StartsWith("injection-", StringComparison.Ordinal)).ToList();

        injections.Select(c => c.Key).Should().BeEquivalentTo(["injection-in-a-tag", "injection-in-a-resource-name", "injection-in-a-description"]);
        foreach (var (id, c) in injections)
        {
            Strings(c["expect"]!["lists"]!["findings[].id"]!["includes"]).Should().Contain("F-FAILED-RUN-ALERT", id);
            Strings(c["expect"]!["mustNotContain"]).Should().NotBeEmpty(id);
            c["setup"]!["seed"]!.GetValue<string>().Should().NotBeEmpty(id);
        }
    }
}
