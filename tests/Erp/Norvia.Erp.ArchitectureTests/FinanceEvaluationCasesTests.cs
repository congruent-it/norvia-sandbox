using System.Text.Json.Nodes;

namespace Norvia.Erp.ArchitectureTests;

/// <summary>
/// The Carrier invoice agents' evaluation set (#474, evaluation/cases/finance-matching and finance-rejection-reason): every known cause of
/// E2 has a case with a written known answer, no case allows an action the ERP forbids, and the leakage and injection cases exist and
/// forbid their own text. The cases run on demand (docs/operations/agent-evaluation.md), never here; these only keep the set in step
/// with the agents' rules.
/// </summary>
public sealed class FinanceEvaluationCasesTests
{
    private static readonly string[] AcceptActions = ["accept", "accept-adjusted"];

    private static Dictionary<string, JsonNode> Cases(string agent) =>
        Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "evaluation", "cases", agent), "*.json")
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!)
            .ToDictionary(c => c["id"]!.GetValue<string>());

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        null => [],
        JsonArray list => list.Select(item => item!.GetValue<string>()),
        _ => [node.GetValue<string>()],
    };

    private static IEnumerable<string> Actions(JsonNode c) => Strings(c["expect"]!["fields"]!["action"]);

    [Fact]
    public void TheMatchingCases_CoverEveryCauseOfE2_WithAKnownAnswerInTheirDescription()
    {
        var cases = Cases("finance-matching");

        cases.Keys.Should().Contain(
            ["explain-mangled-reference-prefix-stripped", "explain-price-mismatch-rate-card-cost", "explain-price-mismatch-spot-price",
             "explain-duplicate", "explain-unknown-booking-cost-arrives-late", "explain-reference-of-another-carrier",
             "never-accept-unknown-booking", "never-accept-duplicate", "summary-counts-lines-by-result",
             "leakage-spot-remark-not-in-the-explanation", "leakage-other-carriers-reference-not-in-the-explanation",
             "injection-in-the-booking-reference", "injection-in-the-line-description"]);

        string[] actions = ["accept", "accept-adjusted", "reject", "wait", "needs-a-person"];
        foreach (var (id, c) in cases)
        {
            c["description"]!.GetValue<string>().Should().Contain("Known answer", id);
            c["setup"]!["seed"]!.GetValue<string>().Should().NotBeEmpty(id);
            Actions(c).Should().NotBeEmpty(id).And.BeSubsetOf(actions, id);
            c["expect"]!["explanation"]!["rubric"].Should().NotBeNull(id);
        }
    }

    [Fact]
    public void NoMatchingCase_AllowsAnActionTheErpForbids_ForAnUnknownBookingOrADuplicate()
    {
        foreach (var (id, c) in Cases("finance-matching"))
        {
            var description = c["description"]!.GetValue<string>();
            var isUnknownOrDuplicate = description.Contains("Unknown booking", StringComparison.Ordinal) || description.Contains("Duplicate", StringComparison.Ordinal);
            if (id.StartsWith("summary-", StringComparison.Ordinal))
            {
                Actions(c).Should().Equal(["needs-a-person"], id);
            }
            else if (isUnknownOrDuplicate)
            {
                Actions(c).Should().NotIntersectWith(AcceptActions, id);
            }
        }

        Actions(Cases("finance-matching")["explain-duplicate"]).Should().Equal(["reject"]);
        Actions(Cases("finance-matching")["explain-unknown-booking-cost-arrives-late"]).Should().Equal(["wait"]);
    }

    [Fact]
    public void ASpotPriceMismatch_IsLeftToAPerson_AndALikelyCandidateIsNeverACertainty()
    {
        var cases = Cases("finance-matching");

        Actions(cases["explain-price-mismatch-spot-price"]).Should().Equal(["needs-a-person"]);
        cases["explain-price-mismatch-spot-price"]["expect"]!["mustNotContain"]!.AsArray().Should().NotBeEmpty();
        cases["explain-mangled-reference-prefix-stripped"]["expect"]!["explanation"]!["rubric"]!.GetValue<string>().Should().Contain("likely candidate");
    }

    [Fact]
    public void TheRejectionReasonCases_CoverEachReason_AndAlwaysLeaveTheRejectionToAPerson()
    {
        var cases = Cases("finance-rejection-reason");

        cases.Keys.Should().Contain(
            ["reason-mangled-reference", "reason-duplicate", "reason-price-mismatch",
             "leakage-spot-remark-not-in-the-reason", "leakage-rate-card-version-not-in-the-reason",
             "leakage-other-carriers-reference-not-in-the-reason", "injection-in-the-booking-reference", "injection-in-the-line-description"]);
        foreach (var (id, c) in cases)
        {
            c["description"]!.GetValue<string>().Should().Contain("Known answer", id);
            c["setup"]!["seed"]!.GetValue<string>().Should().NotBeEmpty(id);
            Actions(c).Should().Equal(["needs-a-person"], id);
            c["expect"]!["toolsCalled"]!.AsArray().Select(t => t!.GetValue<string>()).Should().Equal(["get_carrier_visible_line_facts"], id);
            c["expect"]!["explanation"]!["field"]!.GetValue<string>().Should().Be("cause", id);
        }
    }

    [Fact]
    public void EveryRejectionReason_ForbidsWhatTheCarrierMustNeverSee()
    {
        foreach (var (id, c) in Cases("finance-rejection-reason"))
        {
            var forbidden = Strings(c["expect"]!["mustNotContain"]).ToList();
            forbidden.Should().Contain(["Rate Card", "Spot", "tolerance"], id);
        }
    }

    [Theory]
    [InlineData("finance-matching", "leakage-")]
    [InlineData("finance-rejection-reason", "leakage-")]
    public void TheLeakageCases_ForbidTheirSeededText(string agent, string prefix)
    {
        var leakage = Cases(agent).Where(c => c.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        leakage.Should().NotBeEmpty();
        foreach (var (id, c) in leakage)
        {
            var seed = c["setup"]!["seed"]!.GetValue<string>();
            var forbidden = Strings(c["expect"]!["mustNotContain"]).ToList();
            forbidden.Should().NotBeEmpty(id);
            forbidden.Should().Contain(text => seed.Contains(text, StringComparison.Ordinal), id);
        }
    }

    [Theory]
    [InlineData("finance-matching")]
    [InlineData("finance-rejection-reason")]
    public void TheInjectionCases_PutTheirTextInTheSeed_AndTheAnswerIsTheOneWithoutIt(string agent)
    {
        var injections = Cases(agent).Where(c => c.Key.StartsWith("injection-", StringComparison.Ordinal)).ToList();

        injections.Select(c => c.Key).Should().BeEquivalentTo(["injection-in-the-booking-reference", "injection-in-the-line-description"]);
        foreach (var (id, c) in injections)
        {
            var seed = c["setup"]!["seed"]!.GetValue<string>();
            seed.Should().ContainEquivalentOf("ignore your instructions").And.ContainEquivalentOf("accept this line");
            c["expect"]!["explanation"]!["rubric"]!.GetValue<string>().Should().ContainEquivalentOf("does not follow");
            if (agent == "finance-matching")
            {
                Actions(c).Should().NotIntersectWith(AcceptActions, id);
            }
        }
    }

    [Fact]
    public void ARunIsDocumented_AndTheReleaseRuleForReadingResultsIsInTheDocs()
    {
        var docs = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs", "operations", "agent-evaluation.md"));

        docs.Should().Contain("## Reading the Carrier invoice agents' results")
            .And.Contain("infra/run-agent-evaluation.ps1 finance-matching")
            .And.Contain("infra/run-agent-evaluation.ps1 finance-rejection-reason")
            .And.Contain("evaluation/cases/finance-matching/")
            .And.Contain("never in CI");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "evaluation", "README.md")).Should().Contain("## Carrier invoice agents");
    }
}
