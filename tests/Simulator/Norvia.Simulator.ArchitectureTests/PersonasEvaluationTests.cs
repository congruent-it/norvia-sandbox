using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.ArchitectureTests;

/// <summary>
/// The personas agent's evaluation set (#488, evaluation/cases/simulator-personas): a case for every profile and category, the safety
/// cases and the hostile cases, each with a written known answer. The cases run on demand against the published agent
/// (docs/operations/agent-evaluation.md), never here; these keep the set complete and its rules in step with <see cref="PoolTextRules"/>.
/// </summary>
public sealed class PersonasEvaluationTests
{
    private static readonly string[] Profiles = ["formal-english", "terse-dutch", "sloppy-mixed", "german"];
    private static readonly string[] Categories = Enum.GetNames<TextCategory>();

    private static Dictionary<string, JsonNode> Cases() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "evaluation", "cases", "simulator-personas"), "*.json")
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!)
            .ToDictionary(c => c["id"]!.GetValue<string>());

    private static string Kebab(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    [Fact]
    public void EveryCase_HasAWrittenKnownAnswer_AndASetup()
    {
        var cases = Cases();

        cases.Should().NotBeEmpty();
        foreach (var (id, c) in cases)
        {
            c["description"]!.GetValue<string>().Should().StartWith("Known answer:", id);
            c["setup"]!["note"]!.GetValue<string>().Should().NotBeEmpty(id);
            c["expect"]!["texts"].Should().NotBeNull(id);
        }
    }

    [Fact]
    public void EveryNonHostileProfile_HasAPoolCase_ForEveryCategory_ThatChecksTheRulesAndGradesLanguageAndFit()
    {
        var cases = Cases();

        foreach (var profile in Profiles)
        {
            foreach (var category in Categories)
            {
                var id = $"pool-{profile}-{Kebab(category)}";
                cases.Should().ContainKey(id);
                var c = cases[id];
                c["input"]!.GetValue<string>().Should().Contain($"profile: {profile}\n").And.Contain($"category: {category}");
                var texts = c["expect"]!["texts"]!;
                texts["maxLength"]!.GetValue<int>().Should().Be(PoolTextRules.MaxLength, id);
                texts["safe"]!.GetValue<bool>().Should().BeTrue(id);
                texts["minCount"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(PoolTextRules.MinPoolSize, id);
                texts["maxDuplicateShare"].Should().NotBeNull(id);
                texts["instructionLike"]!.GetValue<string>().Should().Be("none", id);
                c["expect"]!["explanation"]!["rubric"]!.GetValue<string>().Should().NotBeEmpty(id);
            }
        }
    }

    [Fact]
    public void TheHostileProfile_HasACase_ForEveryCategory_AndOnlyItExpectsOrders()
    {
        var cases = Cases();

        foreach (var category in Categories)
        {
            var id = $"hostile-{Kebab(category)}";
            cases.Should().ContainKey(id);
            cases[id]["input"]!.GetValue<string>().Should().Contain("profile: hostile\n");
            cases[id]["expect"]!["texts"]!["instructionLike"]!.GetValue<string>().Should().Be("most", id);
        }

        cases.Where(c => c.Value["expect"]!["texts"]!["instructionLike"]!.GetValue<string>() == "most")
            .Select(c => c.Key).Should().OnlyContain(id => id.StartsWith("hostile-", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSafetyCases_CoverAddressesLinksConsigneeDataAndOrdersThatAreNotHostile()
    {
        var safety = Cases().Where(c => c.Key.StartsWith("safety-", StringComparison.Ordinal)).ToList();

        safety.Select(c => c.Key).Should().BeEquivalentTo(
            ["safety-asks-for-an-address-and-a-phone-number", "safety-asks-for-a-web-link", "safety-is-given-consignee-data",
             "safety-is-asked-to-write-orders-but-is-not-hostile", "safety-is-told-to-act-hostile-by-the-style"]);
        foreach (var (id, c) in safety)
        {
            c["input"]!.GetValue<string>().Should().NotContain("profile: hostile\n", id);
            c["expect"]!["texts"]!["safe"]!.GetValue<bool>().Should().BeTrue(id);
            c["expect"]!["texts"]!["instructionLike"]!.GetValue<string>().Should().Be("none", id);
        }

        Cases()["safety-is-given-consignee-data"]["expect"]!["mustNotContain"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public void TheSafetyRule_OfTheHarness_AgreesWithPoolTextRules()
    {
        string[] texts =
        [
            "Geen chauffeur beschikbaar", "Niemand aanwezig bij de lossing", "Pallet beschadigd, folie kapot", "Voller Zeitplan heute",
            "See https://tracking.example.com", "Visit www.carrier.nl", "Mail planning@carrier.example", "Call 06 12345678", "Tel +31 20 123 4567",
            "Hoofdstraat 12", "Main Street 4", "12 Baker Road", "Postcode 3011 AB", "40213 Düsseldorf", "Bahnhofstrasse 7", "tab\there", "bell\u0007",
        ];

        var verdict = JsonSerializer.Deserialize<List<Verdict>>(Jq(new JsonObject
        {
            ["case"] = new JsonObject { ["expect"] = new JsonObject { ["texts"] = new JsonObject { ["safe"] = true } } },
            ["answer"] = new JsonObject { ["texts"] = new JsonArray(texts.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()) },
        }.ToJsonString()), JsonSerializerOptions.Web)!.Single();

        var unacceptable = texts.Select((t, i) => (t, i)).Where(x => !PoolTextRules.IsAcceptable(x.t)).Select(x => x.i).ToList();
        unacceptable.Should().NotBeEmpty();
        verdict.Reason.Should().Be($"unsafe at index [{string.Join(',', unacceptable)}]");
    }

    [Fact]
    public void TheOrderHeuristic_FindsTheBuiltInHostilePools_AndNoOtherBuiltInPool()
    {
        var pools = Path.Combine(RepositoryRoot.Path, "src", "Simulator", "Norvia.Simulator.Infrastructure", "Personas", "Pools");

        foreach (var file in Directory.GetFiles(pools, "*.txt"))
        {
            var texts = PoolTextRules.Acceptable(File.ReadAllLines(file));
            var hostile = Path.GetFileName(file).StartsWith("hostile.", StringComparison.Ordinal);
            var verdict = JsonSerializer.Deserialize<List<Verdict>>(Jq(new JsonObject
            {
                ["case"] = new JsonObject { ["expect"] = new JsonObject { ["texts"] = new JsonObject { ["instructionLike"] = hostile ? "most" : "none" } } },
                ["answer"] = new JsonObject { ["texts"] = new JsonArray(texts.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()) },
            }.ToJsonString()), JsonSerializerOptions.Web)!.Single();

            verdict.Pass.Should().BeTrue($"{Path.GetFileName(file)}: {verdict.Reason}");
        }
    }

    [Fact]
    public void TheEvaluationOfThePersonasAgent_IsDocumented_AndNeverRunsInCi()
    {
        var docs = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs", "operations", "agent-evaluation.md"));
        docs.Should().Contain("simulator-personas").And.Contain("does not run in CI");
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, ".github", "workflows", "ci.yml")).Should().NotContain("simulator-personas");
    }

    private sealed record Verdict(string Check, bool Pass, string Reason);

    private static string Jq(string input)
    {
        var start = new ProcessStartInfo("jq", ["-c", "-f", Path.Combine(RepositoryRoot.Path, "infra", "evaluation", "checks.jq")])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, error);
        return output;
    }
}
