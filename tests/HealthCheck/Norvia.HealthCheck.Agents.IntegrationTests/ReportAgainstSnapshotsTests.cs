using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Report;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The report over real runs of the app (#402): the comparison of two runs, the severity staying the Check's after the Agent's answer, and the
/// Markdown export holding every Finding with its Check ID, evidence and fix. A re-run after a fix is a run of the next snapshot, as the
/// operator's re-run is a run of the estate once the toggle is put right. Each test starts the app and the stand-in of its own.
/// </summary>
public sealed class ReportAgainstSnapshotsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReRunningAfterTheFixes_ShowsEveryFindingAsFixed_AndNoneAsNewOrUnchanged()
    {
        var previous = await RunAsync("as-found");
        var current = await RunAsync("fixed");

        var comparison = ReportComparison.Between(previous, current);

        previous.Findings.Should().NotBeEmpty();
        comparison.Fixed.Select(finding => finding.Id).Should().BeEquivalentTo(previous.Findings.Select(finding => finding.Id));
        comparison.New.Should().BeEmpty();
        comparison.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public async Task ReRunningAfterOneStaysUnfixed_ShowsOnlyThatFindingAsUnchanged_AndTheRestAsFixed()
    {
        var previous = await RunAsync("as-found");
        var current = await RunAsync("look-alike");

        var comparison = ReportComparison.Between(previous, current);

        comparison.Unchanged.Select(finding => finding.AsFoundToggle).Should().Equal("shipperTrackingCached");
        comparison.Fixed.Select(finding => finding.Id).Should().BeEquivalentTo(previous.Findings.Select(finding => finding.Id).Except(comparison.Unchanged.Select(finding => finding.Id)));
        comparison.New.Should().BeEmpty();
    }

    [Fact]
    public async Task ReRunningAfterAToggleIsTurnedOn_ShowsItsFindingAsNew()
    {
        var previous = await RunAsync("fixed");
        var current = await RunAsync("look-alike");

        var comparison = ReportComparison.Between(previous, current);

        comparison.New.Select(finding => finding.AsFoundToggle).Should().Equal("shipperTrackingCached");
        comparison.Fixed.Should().BeEmpty();
        comparison.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public async Task ReRunningTheSameEstate_ChangesNothing()
    {
        await using var host = new HealthCheckAgentHost("as-found");

        var previous = await RunAsync(host);

        var comparison = ReportComparison.Between(previous, await RunAsync(host));

        comparison.New.Should().BeEmpty();
        comparison.Fixed.Should().BeEmpty();
        comparison.Unchanged.Select(finding => finding.Id).Should().BeEquivalentTo(previous.Findings.Select(finding => finding.Id));
    }

    [Fact]
    public async Task AfterTheAgentsAnswer_EveryFindingKeepsItsChecksSeverity_WhateverOrderTheAgentPutThemIn()
    {
        await using var host = new HealthCheckAgentHost("as-found");
        var report = await RunAsync(host);
        var narration = await NarrateAsync(host);

        var groups = SeverityGroup.By(report.Findings, narration.Items.Select(item => item.Id).Reverse().ToList());

        groups.SelectMany(group => group.Findings).Select(finding => finding.Id).Should().BeEquivalentTo(report.Findings.Select(finding => finding.Id));
        foreach (var group in groups)
        {
            group.Findings.Should().OnlyContain(finding => finding.Severity == group.Severity);
            group.Findings.Should().OnlyContain(finding => SeverityOfCheck(finding.CheckId) == group.Severity, "the severity is the Check's, not the Agent's");
        }

        groups.Select(group => group.Severity).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task TheMarkdownExport_HasEveryFindingWithItsCheckIdSeverityEvidenceAndFix()
    {
        await using var host = new HealthCheckAgentHost("as-found");
        var report = await RunAsync(host);

        var markdown = MarkdownReport.Render(report);

        report.Findings.Should().NotBeEmpty();
        markdown.Should().Contain($"Findings: {report.Findings.Count}");
        foreach (var finding in report.Findings)
        {
            markdown.Should().Contain($"### {finding.Id}:").And.Contain($"- Check: `{finding.CheckId}`").And.Contain($"- Severity: {finding.Severity}").And.Contain($"- Fix: {finding.Fix}");
            foreach (var evidence in finding.Evidence)
            {
                markdown.Should().Contain($"{evidence.Subject}: {evidence.Detail}");
            }
        }
    }

    [Fact]
    public async Task TheMarkdownExport_WithTheAgentsAnswerAndAComparison_KeepsTheChecksSeverity_AndMarksTheAssistantsWords()
    {
        await using var host = new HealthCheckAgentHost("as-found");
        var report = await RunAsync(host);
        var narration = await NarrateAsync(host);
        var comparison = ReportComparison.Between(await RunAsync("fixed"), report);

        var markdown = MarkdownReport.Render(report, narration, comparison);

        markdown.Should().Contain($"Against the previous run: {report.Findings.Count} new, 0 fixed, 0 unchanged.");
        foreach (var finding in report.Findings)
        {
            markdown.Should().Contain($"### {finding.Id}: {finding.Title} (new)").And.Contain($"- Severity: {finding.Severity}");
        }

        narration.Items.Should().NotBeEmpty();
        markdown.Split("Why it matters (the assistant's words)").Length.Should().Be(narration.Items.Count + 1, "each explained Finding carries the assistant's words, once");
    }

    [Fact]
    public async Task TheMarkdownExport_OfTheFixedEstate_SaysEveryCheckPassed()
    {
        var report = await RunAsync("fixed");

        MarkdownReport.Render(report).Should().Contain("No Findings. Every Check passed.").And.NotContain("### ");
    }

    [Fact]
    public async Task ExplainingOneFinding_AnswersForThatFindingAlone_AndItsSeverityIsStillTheChecks()
    {
        await using var host = new HealthCheckAgentHost("as-found");
        var report = await RunAsync(host);
        var finding = report.Findings.Single(candidate => candidate.AsFoundToggle == "carrierSecretInlined");

        using var scope = host.AppServices.CreateScope();
        var answer = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ExplainFindingQuery(Guid.NewGuid().ToString(), finding.Id, FindingQuestion.WhyIsThisAProblem), Ct);

        answer.IsSuccess.Should().BeTrue(string.Join("; ", answer.Errors));
        answer.Value.Id.Should().Be(finding.Id);
        MarkdownReport.Render(report, new Narration("One.", [answer.Value])).Should().Contain($"- Severity: {nameof(Severity.Critical)}");
        finding.Severity.Should().Be(SeverityOfCheck(finding.CheckId));
    }

    private static Severity SeverityOfCheck(string checkId) => Checklist.Default.Checks.Single(check => check.Id == checkId).Severity;

    private static async Task<HealthCheckReport> RunAsync(string snapshot)
    {
        await using var host = new HealthCheckAgentHost(snapshot);
        return await RunAsync(host);
    }

    private static async Task<HealthCheckReport> RunAsync(HealthCheckAgentHost host)
    {
        using var scope = host.AppServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RunHealthCheckQuery(), Ct);
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private static async Task<Narration> NarrateAsync(HealthCheckAgentHost host)
    {
        using var scope = host.AppServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new NarrateHealthCheckQuery(Guid.NewGuid().ToString()), Ct);
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }
}
