using Microsoft.Playwright;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.DeadLetters;

// Every test names its own queue, so the shared fakes never answer for another test. The panel's own list is read by the
// tests of this class only, and the tests of a class run one after the other.
public sealed class DeadLetterJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    private const string Resubmitter = "Iris Operator";

    [Fact]
    public async Task AnOperatorWithoutTheResubmitRole_SeesAMessageButNoWayToResubmitOrDiscardIt()
    {
        var queue = QueueName();
        var message = Message(11, DateTimeOffset.UtcNow.AddMinutes(-20));
        PeekReturns(queue, message);
        Console.Port<IDeadLetterQueues>().OpenAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>())
            .Returns(new RecordedDeadLetterBody(message, """{"event":"POD"}""", new Dictionary<string, string> { ["carrier"] = "van-dijk" }));
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");
        await Page.GotoAsync($"/dead-letter-queues/{queue}");

        await Expect(Page.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(0);
        await MessagesTable.GetByRole(AriaRole.Button).ClickAsync();

        await Expect(Page.GetByText("\"event\": \"POD\"")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Resubmit" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Discard" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnOperatorDiscardingAMessage_IsAskedForAReason_AndTheDiscardIsAuditedWithIt()
    {
        var queue = QueueName();
        var message = Message(21, DateTimeOffset.UtcNow.AddMinutes(-20));
        PeekReturns(queue, message);
        Console.Port<IDeadLetterQueues>().OpenAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>())
            .Returns(new RecordedDeadLetterBody(message, "{}", new Dictionary<string, string>()));
        Console.Port<IDeadLetterQueues>().DiscardAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>())
            .Returns(new DiscardedDeadLetter(queue, "message-21", message.CorrelationId, []));
        await SignInAsync(Resubmitter, "IntegrationConsole.Read", "IntegrationConsole.Resubmit");
        await Page.GotoAsync($"/dead-letter-queues/{queue}");
        await MessagesTable.GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Discard" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm discard" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Give a reason for discarding the message.");
        await Console.Port<IDeadLetterQueues>().DidNotReceive().DiscardAsync(queue, message.SequenceNumber, Arg.Any<CancellationToken>());

        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Reason for discarding" }).FillAsync("The Carrier sent it twice.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm discard" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Discarded message 21 of {queue}." })).ToBeVisibleAsync();
        await Console.Port<IAuditLog>().Received(1).WriteAsync(
            Arg.Is<AuditEvent>(audited => audited.Entity == queue && audited.Action == AuditActions.Discard && audited.Actor == Resubmitter
                && audited.Reason == "The Carrier sent it twice."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOperatorResubmittingASelection_SendsItInOneActionOldestFirst_AndSeesEachOutcome()
    {
        var queue = QueueName();
        var now = DateTimeOffset.UtcNow;
        var oldest = Message(31, now.AddMinutes(-30));
        var next = Message(32, now.AddMinutes(-20));
        var failing = Message(33, now.AddMinutes(-10));
        var untouched = Message(34, now.AddMinutes(-5));
        PeekReturns(queue, untouched, failing, oldest, next);
        var queues = Console.Port<IDeadLetterQueues>();
        queues.ResubmitAsync(queue, oldest.SequenceNumber, Arg.Any<CancellationToken>()).Returns(Resubmitted(queue, oldest));
        queues.ResubmitAsync(queue, next.SequenceNumber, Arg.Any<CancellationToken>()).Returns((ResubmittedDeadLetter?)null);
        queues.ResubmitAsync(queue, failing.SequenceNumber, Arg.Any<CancellationToken>())
            .Returns<ResubmittedDeadLetter?>(_ => throw new InvalidOperationException("The queue is down."));
        await SignInAsync(Resubmitter, "IntegrationConsole.Read", "IntegrationConsole.Resubmit");
        await Page.GotoAsync($"/dead-letter-queues/{queue}");
        await SelectAsync(33);
        await SelectAsync(32);
        await SelectAsync(31);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Resubmit selected" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm resubmit of selection" }).ClickAsync();

        var outcomes = Page.GetByRole(AriaRole.Region, new() { Name = "Outcome per message" });
        await Expect(outcomes).ToContainTextAsync("Resubmit of 3 selected messages");
        await Expect(outcomes.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Message 31:" })).ToContainTextAsync("done.");
        await Expect(outcomes.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Message 32:" })).ToContainTextAsync("no longer on the queue");
        await Expect(outcomes.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Message 33:" })).ToContainTextAsync("failed.");
        await Expect(MessagesTable.GetByRole(AriaRole.Row).Filter(new() { HasText = failing.CorrelationId })).ToHaveCountAsync(1);
        await Expect(MessagesTable.GetByRole(AriaRole.Row).Filter(new() { HasText = oldest.CorrelationId })).ToHaveCountAsync(0);
        Received.InOrder(() =>
        {
            queues.ResubmitAsync(queue, oldest.SequenceNumber, Arg.Any<CancellationToken>());
            queues.ResubmitAsync(queue, next.SequenceNumber, Arg.Any<CancellationToken>());
            queues.ResubmitAsync(queue, failing.SequenceNumber, Arg.Any<CancellationToken>());
        });
        await queues.DidNotReceive().ResubmitAsync(queue, untouched.SequenceNumber, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOperatorOpeningADeadLetterQueueWhileItLoads_SeesProgress_ThenItsMessages()
    {
        var queue = QueueName();
        var message = Message(41, DateTimeOffset.UtcNow.AddMinutes(-3));
        var peeked = new TaskCompletionSource<IReadOnlyList<RecordedDeadLetter>?>();
        Console.Port<IDeadLetterQueues>().PeekAsync(queue, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => peeked.Task);
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");

        await Page.GotoAsync($"/dead-letter-queues/{queue}");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Loading the dead-lettered messages…");

        peeked.SetResult([message]);

        await Expect(MessagesTable.GetByRole(AriaRole.Row).Filter(new() { HasText = message.CorrelationId })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task AnOperatorOpeningAnEmptyDeadLetterQueue_IsToldItHoldsNoMessages()
    {
        var queue = QueueName();
        PeekReturns(queue);
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");

        await Page.GotoAsync($"/dead-letter-queues/{queue}");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"{queue} has no dead-lettered messages.");
    }

    [Fact]
    public async Task AnOperatorOpeningADeadLetterQueueTheServiceBusCantBeReadFor_IsToldItCouldntBeListed()
    {
        var queue = QueueName();
        Console.Port<IDeadLetterQueues>().PeekAsync(queue, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RecordedDeadLetter>?>(_ => throw new InvalidOperationException("The Service Bus is down."));
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");

        await Page.GotoAsync($"/dead-letter-queues/{queue}");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The dead-lettered messages couldn't be listed.");
    }

    [Fact]
    public async Task TheDeadLetterQueuesPanel_ShowsLoadingThenQueuesWithMessagesFirst()
    {
        var loaded = new TaskCompletionSource<IReadOnlyList<RecordedDeadLetters>>();
        var holding = QueueName();
        var empty = QueueName();
        Console.Port<IDeadLetterQueues>().DeadLetterQueuesAsync(Arg.Any<CancellationToken>()).Returns(_ => loaded.Task);
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");

        await Page.GotoAsync("/dead-letter-queues");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Loading the dead-letter queues…");

        loaded.SetResult([new RecordedDeadLetters(empty, 0, null), new RecordedDeadLetters(holding, 2, DateTimeOffset.UtcNow.AddHours(-3))]);

        var rows = Page.GetByRole(AriaRole.Table, new() { Name = "Dead-letter queues" }).GetByRole(AriaRole.Row);
        await Expect(rows.Filter(new() { HasText = holding })).ToContainTextAsync("3 h");
        await Expect(rows.Nth(1)).ToContainTextAsync(holding);
        await Expect(rows.Nth(2)).ToContainTextAsync(empty);
    }

    [Fact]
    public async Task TheDeadLetterQueuesPanel_WithNoQueues_SaysSo()
    {
        Console.Port<IDeadLetterQueues>().DeadLetterQueuesAsync(Arg.Any<CancellationToken>()).Returns([]);
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");

        await Page.GotoAsync("/dead-letter-queues");

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("The Service Bus has no queues or subscriptions.");
    }

    [Fact]
    public async Task TheDeadLetterQueuesPanel_WhenTheServiceBusCantBeRead_ShowsAnError()
    {
        Console.Port<IDeadLetterQueues>().DeadLetterQueuesAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RecordedDeadLetters>>(_ => throw new InvalidOperationException("The Service Bus is down."));
        await SignInAsync(Resubmitter, "IntegrationConsole.Read");

        await Page.GotoAsync("/dead-letter-queues");

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The dead-letter queues couldn't be listed.");
    }

    // The page re-renders the checkbox from its own selection, so the state follows the click rather than flips with it.
    private async Task SelectAsync(long sequenceNumber)
    {
        var checkbox = Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select message {sequenceNumber}" });
        await checkbox.ClickAsync();
        await Expect(checkbox).ToBeCheckedAsync();
    }

    private ILocator MessagesTable => Page.GetByRole(AriaRole.Table, new() { Name = "Dead-lettered messages" });

    private static string QueueName() => $"queue-{Guid.NewGuid():N}";

    private static RecordedDeadLetter Message(long sequenceNumber, DateTimeOffset enqueuedAt) =>
        new(sequenceNumber, enqueuedAt, "MaxDeliveryCountExceeded", "The TMS was unavailable.", 10, Guid.NewGuid().ToString(),
            [new BusinessKey(BusinessKeyKind.ShipmentNumber, $"SH-{Guid.NewGuid():N}")]);

    private static ResubmittedDeadLetter Resubmitted(string queue, RecordedDeadLetter message) =>
        new(queue, $"original-{message.SequenceNumber}", $"copy-{message.SequenceNumber}", message.CorrelationId, message.BusinessKeys);

    private void PeekReturns(string queue, params RecordedDeadLetter[] messages) =>
        Console.Port<IDeadLetterQueues>().PeekAsync(queue, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(messages);
}
