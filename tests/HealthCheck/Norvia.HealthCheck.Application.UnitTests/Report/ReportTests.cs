using Norvia.Agents;
using Evidence = Norvia.HealthCheck.Application.Checks.Evidence;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Report;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Application.UnitTests.Report;

public sealed class ReportTests
{
    private static readonly DateTimeOffset RanAt = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    private static Finding Found(string checkId, Severity severity, string detail = "No alert.") =>
        new($"F-{checkId}", checkId, $"{checkId} title", $"{checkId} rule", severity, [new Evidence("billing", detail)], $"{checkId}Toggle", $"Remove {checkId}Toggle.");

    private static HealthCheckReport Run(params Finding[] findings) => new("as-found", RanAt, 5, findings);

    [Fact]
    public void SeverityGroup_GroupsMostSevereFirst_AndLeavesOutASeverityWithNone()
    {
        var groups = SeverityGroup.By([Found("A", Severity.Low), Found("B", Severity.Critical), Found("C", Severity.Low)]);

        groups.Select(group => group.Severity).Should().Equal(Severity.Critical, Severity.Low);
        groups[1].Findings.Select(finding => finding.Id).Should().Equal("F-A", "F-C");
    }

    [Fact]
    public void SeverityGroup_FollowsTheAgentsOrderWithinASeverity_AndNeverMovesAFindingToAnotherGroup()
    {
        var groups = SeverityGroup.By([Found("A", Severity.High), Found("B", Severity.High), Found("C", Severity.Low)], ["F-C", "F-B", "F-B"]);

        groups.Select(group => group.Severity).Should().Equal(Severity.High, Severity.Low);
        groups[0].Findings.Select(finding => finding.Id).Should().Equal("F-B", "F-A");
        groups.SelectMany(group => group.Findings).Should().OnlyContain(finding => finding.Severity == Found(finding.CheckId, finding.Severity).Severity);
    }

    [Fact]
    public void Between_TellsNewFromFixedFromUnchanged()
    {
        var previous = Run(Found("KEPT", Severity.High, "before"), Found("GONE", Severity.High));
        var current = Run(Found("KEPT", Severity.High, "after"), Found("NEW", Severity.Low));

        var comparison = ReportComparison.Between(previous, current);

        comparison.New.Select(finding => finding.Id).Should().Equal("F-NEW");
        comparison.Fixed.Select(finding => finding.Id).Should().Equal("F-GONE");
        comparison.Unchanged.Should().ContainSingle().Which.Evidence[0].Detail.Should().Be("after");
    }

    [Fact]
    public void Between_AfterFixingTheToggle_ShowsTheFindingAsFixed()
    {
        var comparison = ReportComparison.Between(Run(Found("DLQ-ALERT", Severity.High)), Run());

        comparison.Fixed.Should().ContainSingle().Which.Id.Should().Be("F-DLQ-ALERT");
        comparison.New.Should().BeEmpty();
        comparison.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Render_HasEveryFindingWithItsCheckIdSeverityEvidenceAndFix()
    {
        var report = Run(Found("A", Severity.Low), Found("B", Severity.Critical));

        var markdown = MarkdownReport.Render(report);

        markdown.Should().Contain("# Estate health check").And.Contain("Findings: 2");
        markdown.IndexOf("## Critical", StringComparison.Ordinal).Should().BeLessThan(markdown.IndexOf("## Low", StringComparison.Ordinal));
        foreach (var finding in report.Findings)
        {
            markdown.Should().Contain($"### {finding.Id}").And.Contain($"- Check: `{finding.CheckId}`").And.Contain($"- Severity: {finding.Severity}")
                .And.Contain("billing: No alert.").And.Contain($"- Fix: Remove {finding.CheckId}Toggle.");
        }
    }

    [Fact]
    public void Render_WithNoFindings_SaysEveryCheckPassed() =>
        MarkdownReport.Render(Run()).Should().Contain("No Findings. Every Check passed.");

    [Fact]
    public void Render_WithANarration_MarksTheAssistantsWords_AndKeepsTheChecksSeverity()
    {
        var narration = new Narration("Two problems.", [new NarratedItem("F-A", "Nobody is told.", "r1", "run_checks")]);

        var markdown = MarkdownReport.Render(Run(Found("A", Severity.Low), Found("B", Severity.High)), narration);

        markdown.Should().Contain("Assistant's ordering: Two problems.").And.Contain("Why it matters (the assistant's words): Nobody is told. [r1, run_checks]");
        markdown.Should().Contain("- Severity: Low", because: "the agent's answer cannot change a severity");
        markdown.Split("Why it matters").Length.Should().Be(2, because: "only the Finding the assistant explained carries its words");
    }

    [Fact]
    public void Render_WithAComparison_ListsFixedAndMarksNew()
    {
        var previous = Run(Found("GONE", Severity.High));
        var current = Run(Found("NEW", Severity.High));

        var markdown = MarkdownReport.Render(current, comparison: ReportComparison.Between(previous, current));

        markdown.Should().Contain("1 new, 1 fixed, 0 unchanged").And.Contain("Fixed since the previous run").And.Contain("- F-GONE: GONE title").And.Contain("F-NEW: NEW title (new)");
    }

    [Fact]
    public void Render_KeepsEvidenceOnOneLine_SoItCannotStartAHeading()
    {
        var markdown = MarkdownReport.Render(Run(Found("A", Severity.High, "line\n# Injected")));

        markdown.Should().NotContain("\n# Injected");
    }
}
