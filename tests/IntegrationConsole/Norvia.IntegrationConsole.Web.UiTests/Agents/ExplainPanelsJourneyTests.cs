using Ardalis.Result;
using Microsoft.Playwright;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Agents;

/// <summary>
/// The Explain journeys of the Failed runs and Dead-letter queues panels with <c>Agents:Enabled</c> on and the Agent scripted. The
/// panels list what the estate-wide fakes answer, so only the tests of this class set them, and the tests of a class run one after the other.
/// Every test names its own flow or queue, so the scripted answers never meet another test's question.
/// </summary>
public sealed class ExplainPanelsJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    protected override bool AgentsEnabled => true;

    [Fact]
    public async Task AnOperatorExplainingAFailedRun_SeesTheCause_TheCitedEvidence_AndASuggestedAction()
    {
        var (flow, start) = FailedRunListed();
        Agent(AgentQuestions.ExplainFailedRun(flow, start)).Returns(Result.Success(new Suggestion(
            "The TMS answered 503, so the Submit_to_TMS step failed.",
            [new Evidence("The run failed at Submit_to_TMS with ServiceUnavailable.", "r1", "list_failed_runs")],
            "Wait for the TMS to recover, then resubmit the Shipment update from the Partner portal.",
            Confidence.Medium)));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync("/failed-runs");

        await FailedRunRow(flow).GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();

        var explanation = FailedRunRow(flow).GetByTestId("explanation");
        await Expect(explanation).ToContainTextAsync("The TMS answered 503, so the Submit_to_TMS step failed.");
        await Expect(explanation).ToContainTextAsync("The run failed at Submit_to_TMS with ServiceUnavailable.");
        await Expect(explanation.Locator("cite")).ToHaveTextAsync("[r1, list_failed_runs]");
        await Expect(explanation).ToContainTextAsync("Wait for the TMS to recover");
        await Expect(explanation).ToContainTextAsync("Confidence: medium");
    }

    [Fact]
    public async Task AnOperatorExplainingADeadLetteredMessage_SeesTheCause_TheCitedEvidence_AndASuggestedAction()
    {
        var (queue, message) = DeadLetterListed(41);
        Agent(AgentQuestions.ExplainDeadLetter(queue, message.SequenceNumber)).Returns(Result.Success(new Suggestion(
            "The delivery count reached 10 because the TMS was unavailable.",
            [new Evidence("The message was dead-lettered with MaxDeliveryCountExceeded.", "r1", "peek_dead_letters")],
            "Resubmit the message once the TMS is back.",
            Confidence.High)));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync($"/dead-letter-queues/{queue}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explain dead-lettered message 41" }).ClickAsync();

        var explanation = Page.GetByTestId("explanation");
        await Expect(explanation).ToContainTextAsync("The delivery count reached 10 because the TMS was unavailable.");
        await Expect(explanation).ToContainTextAsync("The message was dead-lettered with MaxDeliveryCountExceeded.");
        await Expect(explanation.Locator("cite")).ToHaveTextAsync("[r1, peek_dead_letters]");
        await Expect(explanation).ToContainTextAsync("Resubmit the message once the TMS is back.");
        await Expect(explanation).ToContainTextAsync("Confidence: high");
    }

    [Fact]
    public async Task AnOperatorAskingWhatNeedsAttention_SeesTheQueuesGroupedByCause_AndTheQueueListStays()
    {
        var queue = QueueName();
        Console.Port<IDeadLetterQueues>().DeadLetterQueuesAsync(Arg.Any<CancellationToken>())
            .Returns([new RecordedDeadLetters(queue, 3, DateTimeOffset.UtcNow.AddHours(-2))]);
        Agent(AgentQuestions.WhatNeedsAttention).Returns(Result.Success(new Suggestion(
            $"One cause holds every message: the TMS was unavailable (queue {queue}).",
            [new Evidence($"{queue} holds 3 messages, all MaxDeliveryCountExceeded.", "r1", "peek_dead_letters")],
            "Resubmit the messages of that queue once the TMS is back.",
            Confidence.High)));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync("/dead-letter-queues");

        await Page.GetByRole(AriaRole.Button, new() { Name = "What needs attention" }).ClickAsync();

        var explanation = Page.GetByTestId("explanation");
        await Expect(explanation).ToContainTextAsync($"One cause holds every message: the TMS was unavailable (queue {queue}).");
        await Expect(explanation).ToContainTextAsync($"{queue} holds 3 messages, all MaxDeliveryCountExceeded.");
        await Expect(explanation.Locator("cite")).ToHaveTextAsync("[r1, peek_dead_letters]");
        await Expect(explanation).ToContainTextAsync("Resubmit the messages of that queue once the TMS is back.");
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Dead-letter queues" })).ToContainTextAsync(queue);
    }

    [Fact]
    public async Task AnOperatorWaitingForAnExplanation_SeesTheAssistantReading_ThenTheExplanation()
    {
        var (flow, start) = FailedRunListed();
        var answered = new TaskCompletionSource<Result<Suggestion>>();
        Agent(AgentQuestions.ExplainFailedRun(flow, start)).Returns(_ => answered.Task);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync("/failed-runs");

        await FailedRunRow(flow).GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();

        await Expect(FailedRunRow(flow).GetByRole(AriaRole.Status)).ToContainTextAsync("The assistant is reading…");
        await Expect(FailedRunRow(flow).GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToBeDisabledAsync();

        answered.SetResult(Result.Success(new Suggestion("The TMS answered 503.", [], "Wait and resubmit.", Confidence.Low)));

        await Expect(FailedRunRow(flow).GetByTestId("explanation")).ToContainTextAsync("The TMS answered 503.");
        await Expect(FailedRunRow(flow).GetByRole(AriaRole.Status)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorExplainingAFailedRunWhenTheAgentFails_IsToldTheAssistantIsUnavailable_AndTheRunStaysListed()
    {
        var (flow, start) = FailedRunListed();
        Agent(AgentQuestions.ExplainFailedRun(flow, start)).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync("/failed-runs");

        await FailedRunRow(flow).GetByRole(AriaRole.Button, new() { Name = "Explain" }).ClickAsync();

        await Expect(FailedRunRow(flow).GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(FailedRunRow(flow).GetByTestId("explanation")).ToHaveCountAsync(0);
        await Expect(FailedRunRow(flow)).ToContainTextAsync("ServiceUnavailable: The TMS answered 503.");
        await Expect(FailedRunRow(flow).GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task AnOperatorExplainingADeadLetteredMessageWhenTheAgentFails_IsToldTheAssistantIsUnavailable_AndTheMessageStaysListed()
    {
        var (queue, message) = DeadLetterListed(51);
        Agent(AgentQuestions.ExplainDeadLetter(queue, message.SequenceNumber)).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync($"/dead-letter-queues/{queue}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Explain dead-lettered message 51" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(Page.GetByTestId("explanation")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Dead-lettered messages" })).ToContainTextAsync(message.Reason!);
    }

    [Fact]
    public async Task AnOperatorAskingWhatNeedsAttentionWhenTheAgentFails_IsToldTheAssistantIsUnavailable_AndTheQueueListStays()
    {
        var queue = QueueName();
        Console.Port<IDeadLetterQueues>().DeadLetterQueuesAsync(Arg.Any<CancellationToken>())
            .Returns([new RecordedDeadLetters(queue, 1, DateTimeOffset.UtcNow.AddMinutes(-30))]);
        Agent(AgentQuestions.WhatNeedsAttention).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");
        await Page.GotoAsync("/dead-letter-queues");

        await Page.GetByRole(AriaRole.Button, new() { Name = "What needs attention" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(Page.GetByTestId("explanation")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Dead-letter queues" })).ToContainTextAsync(queue);
    }

    [Fact]
    public async Task TheFailedRunsPanel_WithNoFailedRuns_OffersNoExplainButton()
    {
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/failed-runs");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("No failed runs between");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheDeadLetterQueuesPanel_WithNoQueues_StillOffersWhatNeedsAttention()
    {
        Console.Port<IDeadLetterQueues>().DeadLetterQueuesAsync(Arg.Any<CancellationToken>()).Returns([]);
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/dead-letter-queues");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("The Service Bus has no queues or subscriptions.");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "What needs attention" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheFailedRunsPanel_WhenTheLogicAppsCantBeRead_ShowsAnErrorAndNoExplainButton()
    {
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RecordedFailedRun>>(_ => throw new InvalidOperationException("The Logic Apps are unreachable."));
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/failed-runs");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The failed runs couldn't be listed.");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToHaveCountAsync(0);
    }

    private ILocator FailedRunRow(string flow) => Page.GetByRole(AriaRole.Table, new() { Name = "Failed runs found" }).GetByRole(AriaRole.Row).Filter(new() { HasText = flow });

    private static string QueueName() => $"queue-{Guid.NewGuid():N}";

    private (string Flow, DateTimeOffset Start) FailedRunListed()
    {
        var flow = $"flow-{Guid.NewGuid():N}";
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        Console.Port<IFailedRuns>().FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedFailedRun(null, start, flow, Guid.NewGuid().ToString(), "Submit_to_TMS", new StepError("ServiceUnavailable", "The TMS answered 503.")),
        ]);
        return (flow, start);
    }

    private (string Queue, RecordedDeadLetter Message) DeadLetterListed(long sequenceNumber)
    {
        var queue = QueueName();
        var message = new RecordedDeadLetter(sequenceNumber, DateTimeOffset.UtcNow.AddMinutes(-20), "MaxDeliveryCountExceeded", "The TMS was unavailable.", 10,
            Guid.NewGuid().ToString(), [new BusinessKey(BusinessKeyKind.ShipmentNumber, $"SH-{Guid.NewGuid():N}")]);
        Console.Port<IDeadLetterQueues>().PeekAsync(queue, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([message]);
        return (queue, message);
    }

    private Task<Result<Suggestion>> Agent(string question) =>
        Console.Port<IAgentClient>().AskAsync(Arg.Is<AgentRequest>(request => request.Question == question), Arg.Any<CancellationToken>());
}
