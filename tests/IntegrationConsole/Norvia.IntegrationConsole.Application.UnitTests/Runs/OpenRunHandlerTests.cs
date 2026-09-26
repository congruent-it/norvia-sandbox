using Ardalis.Result;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.UnitTests.Runs;

public sealed class OpenRunHandlerTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowRun Run = new("logic-intake-abc123", "transport-order-intake", "08584421797283920101CU00");

    private readonly IWorkflowRuns _runs = Substitute.For<IWorkflowRuns>();

    [Fact]
    public async Task Handle_ForAKeptRun_ShowsItsStepsWithStatusDurationAndError()
    {
        Record(new RecordedRun("Failed", Started, Started.AddSeconds(3),
        [
            Step("manual", "Succeeded", 0, 0.05),
            Step("Map_partner_booking", "Succeeded", 0.1, 0.35),
            Step("Submit_to_TMS", "Failed", 0.4, 2.9, new StepError("ServiceUnavailable", "The TMS answered 503.")),
        ]));

        var run = await OpenAsync();

        run.IsKept.Should().BeTrue();
        run.Status.Should().Be("Failed");
        run.Duration.Should().Be(TimeSpan.FromSeconds(3));
        run.Steps.Should().Equal(
            new RunStep("manual", "Succeeded", Started, TimeSpan.FromMilliseconds(50), null, InputsHidden: false, OutputsHidden: false),
            new RunStep("Map_partner_booking", "Succeeded", Started.AddSeconds(0.1), TimeSpan.FromMilliseconds(250), null, false, false),
            new RunStep("Submit_to_TMS", "Failed", Started.AddSeconds(0.4), TimeSpan.FromSeconds(2.5),
                new StepError("ServiceUnavailable", "The TMS answered 503."), false, false));
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(730, 365)]
    public async Task Handle_ForARunPastItsRetention_SaysItIsNoLongerKept_AndForHowLongRunHistoryKeepsOne(int retentionDays, int runHistoryDays)
    {
        // Its hop stays on the Message trail, which Application Insights keeps for its own period.
        Record(null);

        var run = await OpenAsync(retentionDays);

        run.Should().BeEquivalentTo(new RunDetail(Run, IsKept: false, null, null, null, [], runHistoryDays));
    }

    [Fact]
    public async Task Handle_ForStepsRecordedOutOfOrder_ShowsThemInTheOrderTheyStarted_NotRunStepsLast()
    {
        Record(new RecordedRun("Succeeded", Started, Started.AddSeconds(1),
        [
            Step("Relay_outcome", "Succeeded", 0.5, 0.6),
            Step("Handle_refusal", "Skipped", 0, 0) with { Start = null, End = null },
            Step("manual", "Succeeded", 0, 0.05),
            Step("Map_partner_booking", "Succeeded", 0.1, 0.35),
        ]));

        var run = await OpenAsync();

        run.Steps.Select(step => step.Name).Should().Equal("manual", "Map_partner_booking", "Relay_outcome", "Handle_refusal");
        run.Steps[^1].Duration.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForAStepWithSecureInputsOrOutputs_MarksThemHidden()
    {
        Record(new RecordedRun("Succeeded", Started, Started.AddSeconds(1),
        [
            Step("Get_carrier_credentials", "Succeeded", 0, 0.1) with { SecureOutputs = true },
            Step("Call_carrier", "Succeeded", 0.2, 0.5) with { SecureInputs = true },
        ]));

        var run = await OpenAsync();

        run.Steps.Select(step => (step.Name, step.InputsHidden, step.OutputsHidden)).Should().Equal(
            ("Get_carrier_credentials", false, true),
            ("Call_carrier", true, false));
    }

    [Theory]
    [InlineData("", "transport-order-intake", "0858")]
    [InlineData("logic-intake", " ", "0858")]
    [InlineData("logic-intake", "transport-order-intake", "")]
    public async Task Handle_WithoutTheLogicAppWorkflowAndRunId_IsInvalid_AndReadsNothing(string logicApp, string workflow, string runId)
    {
        var result = await Handler().Handle(new OpenRunQuery(logicApp, workflow, runId), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _runs.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_WithSpacesAroundTheRun_ReadsTheRunTrimmed()
    {
        Record(null);

        await Handler().Handle(new OpenRunQuery($" {Run.LogicApp} ", $"{Run.Workflow} ", $" {Run.RunId}"), TestContext.Current.CancellationToken);

        await _runs.Received(1).RunAsync(Run, Arg.Any<CancellationToken>());
    }

    private void Record(RecordedRun? run) =>
        _runs.RunAsync(Run, Arg.Any<CancellationToken>()).Returns(run);

    private static RecordedStep Step(string name, string status, double startSecond, double? endSecond, StepError? error = null) =>
        new(name, status, Started.AddSeconds(startSecond), endSecond is { } end ? Started.AddSeconds(end) : null, error, false, false);

    private async Task<RunDetail> OpenAsync(int retentionDays = 90)
    {
        var result = await Handler(retentionDays).Handle(new OpenRunQuery(Run.LogicApp, Run.Workflow, Run.RunId), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ResultStatus.Ok);
        return result.Value;
    }

    private OpenRunHandler Handler(int retentionDays = 90) =>
        new(_runs, Options.Create(new IntegrationRetention { IntegrationRetentionDays = retentionDays }));
}
