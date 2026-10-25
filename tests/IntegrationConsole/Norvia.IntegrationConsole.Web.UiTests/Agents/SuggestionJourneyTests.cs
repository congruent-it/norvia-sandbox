using Ardalis.Result;
using Microsoft.Playwright;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Agents;

/// <summary>
/// The journeys of a Suggestion that pre-selects Resubmit or Discard on a dead-lettered message, with <c>Agents:Enabled</c> on and
/// the Agent scripted. Every test names its own queue, so the scripted answers never meet another test's question.
/// </summary>
public sealed class SuggestionJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    private const string Operator = "Iris Operator";

    private static readonly AgentRun Run = new("integration-operations", "run-7");

    protected override bool AgentsEnabled => true;

    [Fact]
    public async Task AnOperatorActingOnAResubmitSuggestion_SeesTheMessagePreselected_AndNothingIsSentUntilTheyConfirm()
    {
        var (queue, message) = DeadLetterListed(61);
        Suggests(queue, message, SuggestedActionKind.Resubmit);
        await SignInAsync(Operator, "IntegrationConsole.Read", "IntegrationConsole.Resubmit");
        await ExplainAsync(queue, message);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Review resubmit" }).ClickAsync();

        var confirmation = MessageSection(message).GetByRole(AriaRole.Alertdialog, new() { Name = "Confirm resubmit" });
        await Expect(confirmation).ToContainTextAsync("The assistant suggested this");
        await Expect(confirmation).ToContainTextAsync($"Resubmit message {message.SequenceNumber} to {queue}");
        await Console.Port<IDeadLetterQueues>().DidNotReceive().ResubmitAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>());
        await Console.Port<IAuditLog>().DidNotReceive().WriteAsync(Arg.Is<AuditEvent>(audited => audited.Entity == queue), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOperatorConfirmingAResubmitSuggestion_ResubmitsTheMessage_AndTheAuditNamesTheAgentRun()
    {
        var (queue, message) = DeadLetterListed(71);
        Suggests(queue, message, SuggestedActionKind.Resubmit);
        Console.Port<IDeadLetterQueues>().ResubmitAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>())
            .Returns(new ResubmittedDeadLetter(queue, "original-71", "copy-71", message.CorrelationId, message.BusinessKeys));
        await SignInAsync(Operator, "IntegrationConsole.Read", "IntegrationConsole.Resubmit");
        await ExplainAsync(queue, message);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Review resubmit" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm resubmit" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Resubmitted message {message.SequenceNumber} to {queue}." })).ToBeVisibleAsync();
        await Console.Port<IAuditLog>().Received(1).WriteAsync(
            Arg.Is<AuditEvent>(audited => audited.Entity == queue && audited.Action == AuditActions.Resubmit && audited.Actor == Operator
                && audited.CorrelationId == message.CorrelationId && audited.Agent == Run),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOperatorActingOnADiscardSuggestion_IsStillAskedForAReason_AndTheAuditNamesTheAgentRun()
    {
        var (queue, message) = DeadLetterListed(81);
        Suggests(queue, message, SuggestedActionKind.Discard);
        Console.Port<IDeadLetterQueues>().DiscardAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>())
            .Returns(new DiscardedDeadLetter(queue, "original-81", message.CorrelationId, message.BusinessKeys));
        await SignInAsync(Operator, "IntegrationConsole.Read", "IntegrationConsole.Resubmit");
        await ExplainAsync(queue, message);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Review discard" }).ClickAsync();
        await Expect(MessageSection(message).GetByRole(AriaRole.Alertdialog, new() { Name = "Confirm discard" })).ToContainTextAsync("The assistant suggested this");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm discard" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Give a reason for discarding the message.");
        await Console.Port<IDeadLetterQueues>().DidNotReceive().DiscardAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>());

        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Reason for discarding" }).FillAsync("The Carrier sent it twice.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm discard" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Discarded message {message.SequenceNumber} of {queue}." })).ToBeVisibleAsync();
        await Console.Port<IAuditLog>().Received(1).WriteAsync(
            Arg.Is<AuditEvent>(audited => audited.Entity == queue && audited.Action == AuditActions.Discard && audited.Reason == "The Carrier sent it twice."
                && audited.Agent == Run),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOperatorWithoutTheResubmitRole_SeesTheSuggestedAction_ButNoControlToActOnIt()
    {
        var (queue, message) = DeadLetterListed(91);
        Suggests(queue, message, SuggestedActionKind.Resubmit);
        await SignInAsync(Operator, "IntegrationConsole.Read");

        await ExplainAsync(queue, message);

        await Expect(Page.GetByTestId("action-kind")).ToHaveTextAsync("Resubmit");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Review resubmit" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Review discard" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorExplainingAMessageWhenTheReplyIsOutsideTheSchema_IsToldTheAssistantIsUnavailable_AndCanStillActOnTheirOwn()
    {
        var (queue, message) = DeadLetterListed(101);
        Agent(queue, message).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        await SignInAsync(Operator, "IntegrationConsole.Read", "IntegrationConsole.Resubmit");

        await ExplainAsync(queue, message);

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(Page.GetByTestId("explanation")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Review resubmit" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Dead-lettered messages" })).ToContainTextAsync(message.CorrelationId!);
    }

    private async Task ExplainAsync(string queue, RecordedDeadLetter message)
    {
        await Page.GotoAsync($"/dead-letter-queues/{queue}");
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Explain dead-lettered message {message.SequenceNumber}" }).ClickAsync();
    }

    private ILocator MessageSection(RecordedDeadLetter message) => Page.GetByRole(AriaRole.Region, new() { Name = $"Message {message.SequenceNumber}" });

    private (string Queue, RecordedDeadLetter Message) DeadLetterListed(long sequenceNumber)
    {
        var queue = $"queue-{Guid.NewGuid():N}";
        var message = new RecordedDeadLetter(sequenceNumber, DateTimeOffset.UtcNow.AddMinutes(-20), "MaxDeliveryCountExceeded", "The TMS was unavailable.", 10,
            Guid.NewGuid().ToString(), [new BusinessKey(BusinessKeyKind.ShipmentNumber, $"SH-{Guid.NewGuid():N}")]);
        Console.Port<IDeadLetterQueues>().PeekAsync(queue, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([message]);
        Console.Port<IDeadLetterQueues>().OpenAsync(queue, sequenceNumber, Arg.Any<CancellationToken>())
            .Returns(new RecordedDeadLetterBody(message, "{}", new Dictionary<string, string>()));
        return (queue, message);
    }

    private void Suggests(string queue, RecordedDeadLetter message, SuggestedActionKind action) =>
        Agent(queue, message).Returns(Result.Success(new Suggestion(
            "The TMS was unavailable, and it is back.",
            [new Evidence("The message was dead-lettered with MaxDeliveryCountExceeded.", "r1", "peek_dead_letters")],
            action == SuggestedActionKind.Resubmit ? "Resubmit the message now." : "Discard the message: the Carrier sent it again.",
            Confidence.High,
            action,
            Run)));

    private Task<Result<Suggestion>> Agent(string queue, RecordedDeadLetter message)
    {
        var question = AgentQuestions.ExplainDeadLetter(queue, message.SequenceNumber);
        return Console.Port<IAgentClient>().AskAsync(Arg.Is<AgentRequest>(request => request.Question == question), Arg.Any<CancellationToken>());
    }
}
