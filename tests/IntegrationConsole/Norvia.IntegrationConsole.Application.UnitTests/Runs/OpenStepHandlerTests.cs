using Ardalis.Result;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Steps;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.UnitTests.Runs;

public sealed class OpenStepHandlerTests
{
    private const string StepName = "Map_partner_booking";
    private const string Booking = """{"body":{"partnerReference":"BD-2026-0042","consignee":{"name":"Café De Kroon"}}}""";
    private const string TransportOrder = """{"body":{"transportOrder":{"consignee":{"name":"Café De Kroon"}}}}""";

    private static readonly WorkflowRun Run = new("logic-intake-abc123", "transport-order-intake", "08584421797283920101CU00");

    private readonly IWorkflowRuns _runs = Substitute.For<IWorkflowRuns>();

    [Fact]
    public async Task Handle_ForAStep_ShowsItsInputAndOutputBodiesInFull()
    {
        Record(new RecordedBodies(new RecordedBody(false, Booking), new RecordedBody(false, TransportOrder)));

        var step = await OpenAsync();

        step.Should().BeEquivalentTo(new StepDetail(Run, StepName, IsKept: true, StepBody.Shown(Booking), StepBody.Shown(TransportOrder), 90));
    }

    [Fact]
    public async Task Handle_ForAStepWithSecureInputs_MarksTheInputHidden_AndNeverShowsIt()
    {
        // Even when a body came back, a secure one is never shown.
        Record(new RecordedBodies(new RecordedBody(true, Booking), new RecordedBody(false, TransportOrder)));

        var step = await OpenAsync();

        step.Input.Should().Be(StepBody.Hidden);
        step.Input.Content.Should().BeNull();
        step.Output.Should().Be(StepBody.Shown(TransportOrder));
    }

    [Fact]
    public async Task Handle_ForAStepWithSecureOutputs_MarksTheOutputHidden()
    {
        Record(new RecordedBodies(new RecordedBody(false, Booking), RecordedBody.Hidden));

        var step = await OpenAsync();

        step.Output.Should().Be(StepBody.Hidden);
    }

    [Fact]
    public async Task Handle_ForAStepWithoutABody_SaysThereIsNone()
    {
        // A trigger without outputs, or a step that never ran.
        Record(new RecordedBodies(RecordedBody.None, new RecordedBody(false, "")));

        var step = await OpenAsync();

        step.Input.Should().Be(StepBody.None);
        step.Output.Should().Be(StepBody.None);
    }

    [Fact]
    public async Task Handle_ForARunPastItsRetention_SaysTheBodiesAreNoLongerKept()
    {
        Record(null);

        var step = await OpenAsync(retentionDays: 30);

        step.Should().BeEquivalentTo(new StepDetail(Run, StepName, IsKept: false, StepBody.None, StepBody.None, 30));
    }

    [Theory]
    [InlineData("logic-intake", "transport-order-intake", "0858", " ")]
    [InlineData("logic-intake", "", "0858", StepName)]
    public async Task Handle_WithoutTheRunOrTheStep_IsInvalid_AndFetchesNothing(string logicApp, string workflow, string runId, string step)
    {
        var result = await Handler().Handle(new OpenStepQuery(logicApp, workflow, runId, step), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _runs.DidNotReceiveWithAnyArgs().BodiesAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    private void Record(RecordedBodies? bodies) =>
        _runs.BodiesAsync(Run, StepName, Arg.Any<CancellationToken>()).Returns(bodies);

    private async Task<StepDetail> OpenAsync(int retentionDays = 90)
    {
        var result = await Handler(retentionDays).Handle(
            new OpenStepQuery(Run.LogicApp, Run.Workflow, Run.RunId, $" {StepName} "), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ResultStatus.Ok);
        return result.Value;
    }

    private OpenStepHandler Handler(int retentionDays = 90) =>
        new(_runs, Options.Create(new IntegrationRetention { IntegrationRetentionDays = retentionDays }));
}
