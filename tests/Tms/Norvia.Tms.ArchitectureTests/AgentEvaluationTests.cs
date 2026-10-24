using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The shared evaluation harness (#501): each check passes and fails correctly on sample answers, a malformed case is named, the
/// cases in the repo are well formed, and the harness never runs in CI. The runner itself runs in Azure; these run its jq.
/// </summary>
public sealed class AgentEvaluationTests
{
    private const string Answer = """
        { "cause": { "code": "unmappable-carrier-code" }, "action": { "code": "map-code" },
          "evidence": [ { "claim": "DLV is not a POD code", "source": "get_message_trail hop 3" } ],
          "explanation": "The Carrier sent DLV, which the map does not know." }
        """;

    [Fact]
    public void Fields_PassOnTheKnownAnswer_AndFailOnAnotherCause()
    {
        Check(Case("""{ "fields": { "cause.code": "unmappable-carrier-code" } }"""), Answer)
            .Should().ContainSingle().Which.Pass.Should().BeTrue();

        var wrong = Check(Case("""{ "fields": { "cause.code": "carrier-endpoint-down" } }"""), Answer).Single();

        wrong.Pass.Should().BeFalse();
        wrong.Reason.Should().Contain("unmappable-carrier-code").And.Contain("carrier-endpoint-down");
    }

    [Fact]
    public void Fields_AcceptAnyOneOfAnArray_AndFailWhenTheFieldIsMissing()
    {
        Check(Case("""{ "fields": { "action.code": ["contact-carrier", "map-code"] } }"""), Answer).Single().Pass.Should().BeTrue();
        Check(Case("""{ "fields": { "action.code": ["resubmit"] } }"""), Answer).Single().Pass.Should().BeFalse();
        Check(Case("""{ "fields": { "confidence": "high" } }"""), Answer).Single().Reason.Should().Contain("null");
    }

    [Fact]
    public void Fields_FailWhenTheAnswerIsNotStructured()
    {
        var result = Check(Case("""{ "fields": { "cause.code": "x" } }"""), "just prose", structured: false).Single();

        result.Pass.Should().BeFalse();
        result.Reason.Should().Contain("not a structured result");
    }

    [Fact]
    public void MustNotContain_FailsOnAForbiddenString_WhateverItsCase()
    {
        Check(Case("""{ "mustNotContain": ["Acme Haulage"] }"""), Answer).Single().Pass.Should().BeTrue();

        var leaked = Check(Case("""{ "mustNotContain": ["acme haulage"] }"""), Answer.Replace("The Carrier", "ACME Haulage")).Single();

        leaked.Pass.Should().BeFalse();
    }

    [Fact]
    public void MustCite_PassesWhenAnEvidenceSourceNamesIt_AndFailsOtherwise()
    {
        Check(Case("""{ "mustCite": ["get_message_trail"] }"""), Answer).Single().Pass.Should().BeTrue();
        Check(Case("""{ "mustCite": ["get_run"] }"""), Answer).Single().Pass.Should().BeFalse();
        Check(Case("""{ "mustCite": ["get_message_trail"] }"""), """{ "cause": { "code": "x" } }""").Single().Pass.Should().BeFalse();
    }

    [Fact]
    public void ToolsNotCalled_FailsWhenAForbiddenToolRan()
    {
        var calm = Check(Case("""{ "toolsNotCalled": ["get_run"] }"""), Answer, toolCalls: ["get_message_trail"]).Single();
        var steered = Check(Case("""{ "toolsNotCalled": ["get_run"] }"""), Answer, toolCalls: ["get_message_trail", "get_run"]).Single();

        calm.Pass.Should().BeTrue();
        steered.Pass.Should().BeFalse();
        steered.Reason.Should().Contain("called");
    }

    [Fact]
    public void Explanation_FollowsTheGrade_AndFailsWhenNotGraded()
    {
        var expect = """{ "explanation": { "rubric": "names the unmapped code" } }""";

        Check(Case(expect), Answer, grade: new JsonObject { ["pass"] = true, ["reason"] = "names DLV" }).Single().Pass.Should().BeTrue();
        Check(Case(expect), Answer, grade: new JsonObject { ["pass"] = false, ["reason"] = "vague" }).Single()
            .Should().BeEquivalentTo(new Result("explanation quality", false, "vague"));
        Check(Case(expect), Answer).Single().Reason.Should().Contain("not graded");
    }

    [Fact]
    public void ACaseReportsOneResultPerCheck_AndAFailureDoesNotHideTheOthers()
    {
        var results = Check(
            Case("""{ "fields": { "cause.code": "wrong" }, "mustCite": ["get_message_trail"], "toolsNotCalled": ["get_run"] }"""), Answer);

        results.Select(r => r.Pass).Should().Equal(false, true, true);
    }

    [Theory]
    [InlineData("""{ "id": "Bad Id", "agent": "a", "input": "q", "expect": { "mustCite": ["x"] } }""", "id must be")]
    [InlineData("""{ "id": "a", "input": "q", "expect": { "mustCite": ["x"] } }""", "agent must name")]
    [InlineData("""{ "id": "a", "agent": "a", "expect": { "mustCite": ["x"] } }""", "input must be")]
    [InlineData("""{ "id": "a", "agent": "a", "input": "q", "expect": {} }""", "at least one check")]
    [InlineData("""{ "id": "a", "agent": "a", "input": "q", "expect": { "mustHaveFun": [] } }""", "not a check")]
    [InlineData("""{ "id": "a", "agent": "a", "input": "q", "expect": { "mustCite": "x" } }""", "list of strings")]
    [InlineData("""{ "id": "a", "agent": "a", "input": "q", "expect": { "explanation": {} } }""", "with a rubric")]
    public void AMalformedCase_IsNamed(string caseFile, string problem) =>
        Validate(caseFile).Should().ContainMatch($"*{problem}*");

    [Fact]
    public void AWellFormedCase_HasNoProblem() =>
        Validate(Case("""{ "mustCite": ["x"] }""").ToJsonString()).Should().BeEmpty();

    [Fact]
    public void TheCasesInTheRepo_AreWellFormed_AndLiveInTheirAgentsDirectory()
    {
        var cases = Directory.GetFiles(Path.Combine(RepositoryRoot.Path, "evaluation", "cases"), "*.json", SearchOption.AllDirectories);

        cases.Should().NotBeEmpty();
        foreach (var file in cases)
        {
            var json = File.ReadAllText(file);
            Validate(json).Should().BeEmpty(file);
            JsonNode.Parse(json)!["agent"]!.GetValue<string>().Should().Be(new DirectoryInfo(Path.GetDirectoryName(file)!).Name, file);
        }
    }

    [Fact]
    public void TheHarness_NeverRunsInCi_AndOnlyOnDemand()
    {
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, ".github", "workflows", "ci.yml"))
            .Should().NotContain("run-agent-evaluation").And.NotContain("evaluate-agent");

        var manual = File.ReadAllText(Path.Combine(RepositoryRoot.Path, ".github", "workflows", "evaluate-agent.yml"));
        manual.Should().Contain("workflow_dispatch:").And.Contain("infra/run-agent-evaluation.sh");
        manual.Should().NotContain("push:").And.NotContain("pull_request:").And.NotContain("schedule:");
    }

    [Fact]
    public void TheRunner_ExitsZeroOnFindings_AndNeverEchoesATokenOrASecret()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra", "run-agent-evaluation.sh"));

        script.Should().Contain("store: false").And.Contain("Exit code 0 even when cases fail");
        script.Split('\n').Where(line => line.TrimStart().StartsWith("echo", StringComparison.Ordinal))
            .Should().NotContain(line => line.Contains("$access_token", StringComparison.Ordinal));
    }

    private sealed record Result(string Check, bool Pass, string Reason);

    private static JsonObject Case(string expect) => new()
    {
        ["id"] = "sample",
        ["agent"] = "sample",
        ["input"] = "Why did this trail fail?",
        ["expect"] = JsonNode.Parse(expect),
    };

    private static List<Result> Check(
        JsonObject @case, string answer, bool structured = true, string? text = null, string[]? toolCalls = null, JsonObject? grade = null)
    {
        var run = new JsonObject
        {
            ["case"] = @case,
            ["text"] = text ?? answer,
            ["answer"] = structured ? JsonNode.Parse(answer) : null,
            ["toolCalls"] = new JsonArray((toolCalls ?? []).Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
            ["grade"] = grade,
        };

        var output = Jq("checks.jq", run.ToJsonString());
        return JsonSerializer.Deserialize<List<Result>>(output, JsonSerializerOptions.Web)!;
    }

    private static List<string> Validate(string caseFile) =>
        JsonSerializer.Deserialize<List<string>>(Jq("validate-case.jq", caseFile))!;

    private static string Jq(string program, string input)
    {
        var start = new ProcessStartInfo("jq", ["-c", "-f", Path.Combine(RepositoryRoot.Path, "infra", "evaluation", program)])
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
