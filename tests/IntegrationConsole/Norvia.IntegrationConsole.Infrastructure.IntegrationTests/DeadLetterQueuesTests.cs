using Azure.Messaging.ServiceBus;
using Norvia.IntegrationConsole.Application.DeadLetters;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests;

/// <summary>
/// The console's <see cref="IDeadLetterQueues"/> adapter against the Service Bus emulator. Each test has queues and subscriptions of its own and
/// messages whose correlation ID is new, so none depends on another or on the order they run in.
/// </summary>
public sealed class DeadLetterQueuesTests(ServiceBusFixture serviceBus)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private IDeadLetterQueues Adapter => serviceBus.DeadLetterQueues;

    [Fact]
    public async Task ResubmitAsync_ForAQueueMessage_SendsACopyToItsQueue_WithItsPropertiesAndResubmittedFrom()
    {
        var original = EmulatorMessages.New(NewId(), "the body");
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.ResubmitQueue, null, original);
        var deadLettered = Assert.Single(await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.ResubmitQueue, null));

        var resubmitted = await Adapter.ResubmitAsync(ServiceBusFixture.ResubmitQueue, deadLettered.SequenceNumber, Ct);

        resubmitted.Should().NotBeNull();
        var copy = Assert.Single(await EmulatorMessages.TakeAsync(serviceBus.Client, ServiceBusFixture.ResubmitQueue, null, 1));
        copy.Body.ToString().Should().Be("the body");
        copy.CorrelationId.Should().Be(original.CorrelationId);
        copy.Subject.Should().Be(original.Subject);
        copy.ContentType.Should().Be(original.ContentType);
        copy.MessageId.Should().NotBe(original.MessageId).And.Be(resubmitted!.ResubmittedMessageId);
        copy.ApplicationProperties["resubmittedFrom"].Should().Be(original.MessageId);
        copy.ApplicationProperties["colour"].Should().Be("green");
        copy.ApplicationProperties.Should().NotContainKey("DeadLetterReason");
        (await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.ResubmitQueue, null)).Should().NotContain(message => message.SequenceNumber == deadLettered.SequenceNumber);
    }

    [Fact]
    public async Task ResubmitAsync_ForASubscriptionMessage_ReachesThatSubscriptionOnly()
    {
        var original = EmulatorMessages.New(NewId(), "for a subscription");
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.ResubmitTopic, ServiceBusFixture.SubscriptionA, original);
        // The topic gave the other subscription the original too; it takes that off, so only a resubmitted copy could still be there.
        await EmulatorMessages.TakeAsync(serviceBus.Client, ServiceBusFixture.ResubmitTopic, ServiceBusFixture.SubscriptionB, 1);
        var entity = $"{ServiceBusFixture.ResubmitTopic}/{ServiceBusFixture.SubscriptionA}";
        var deadLettered = Assert.Single(await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.ResubmitTopic, ServiceBusFixture.SubscriptionA));

        var resubmitted = await Adapter.ResubmitAsync(entity, deadLettered.SequenceNumber, Ct);

        resubmitted.Should().NotBeNull();
        var copy = Assert.Single(await EmulatorMessages.TakeAsync(serviceBus.Client, ServiceBusFixture.ResubmitTopic, ServiceBusFixture.SubscriptionA, 1));
        copy.Body.ToString().Should().Be("for a subscription");
        copy.CorrelationId.Should().Be(original.CorrelationId);
        copy.ApplicationProperties["resubmittedFrom"].Should().Be(original.MessageId);
        copy.ApplicationProperties["resubmitTo"].Should().Be(ServiceBusFixture.SubscriptionA);
        var other = await EmulatorMessages.TakeAsync(serviceBus.Client, ServiceBusFixture.ResubmitTopic, ServiceBusFixture.SubscriptionB, 1, wait: TimeSpan.FromSeconds(3));
        other.Should().BeEmpty("the topic's other subscription must not run again");
    }

    [Fact]
    public async Task ResubmitAsync_OnASessionSubscription_KeepsEachSessionId_AndTheOrderOfASelection()
    {
        var correlationId = NewId();
        await EmulatorMessages.DeadLetterAsync(
            serviceBus.Client, ServiceBusFixture.SessionTopic, ServiceBusFixture.SessionSubscription,
            EmulatorMessages.New(correlationId, "A1", "TR-A"), EmulatorMessages.New(correlationId, "B1", "TR-B"),
            EmulatorMessages.New(correlationId, "A2", "TR-A"), EmulatorMessages.New(correlationId, "B2", "TR-B"));
        var entity = $"{ServiceBusFixture.SessionTopic}/{ServiceBusFixture.SessionSubscription}";
        var selection = await Adapter.PeekAsync(entity, 10, Ct);

        foreach (var message in selection!.OrderBy(message => message.EnqueuedAt).ThenBy(message => message.SequenceNumber))
        {
            (await Adapter.ResubmitAsync(entity, message.SequenceNumber, Ct)).Should().NotBeNull();
        }

        var a = await EmulatorMessages.TakeSessionAsync(serviceBus.Client, ServiceBusFixture.SessionTopic, ServiceBusFixture.SessionSubscription, "TR-A", 2);
        var b = await EmulatorMessages.TakeSessionAsync(serviceBus.Client, ServiceBusFixture.SessionTopic, ServiceBusFixture.SessionSubscription, "TR-B", 2);
        a.Select(message => message.Body.ToString()).Should().Equal("A1", "A2");
        b.Select(message => message.Body.ToString()).Should().Equal("B1", "B2");
        a.Concat(b).Should().OnlyContain(message => message.SessionId == (message.Body.ToString().StartsWith('A') ? "TR-A" : "TR-B"));
    }

    [Fact]
    public async Task DiscardAsync_RemovesTheMessageFromItsDeadLetterQueue_AndSendsNothing()
    {
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.DiscardQueue, null, EmulatorMessages.New(NewId(), "to discard"));
        var deadLettered = Assert.Single(await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.DiscardQueue, null));

        var discarded = await Adapter.DiscardAsync(ServiceBusFixture.DiscardQueue, deadLettered.SequenceNumber, Ct);

        discarded.Should().NotBeNull();
        (await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.DiscardQueue, null)).Should().BeEmpty();
        (await EmulatorMessages.TakeAsync(serviceBus.Client, ServiceBusFixture.DiscardQueue, null, 1, wait: TimeSpan.FromSeconds(3))).Should().BeEmpty();
        (await Adapter.DiscardAsync(ServiceBusFixture.DiscardQueue, deadLettered.SequenceNumber, Ct)).Should().BeNull("it is gone");
    }

    // The emulator's management API reports 0 for every runtime count, even the active messages of a queue that has some, so the console
    // lists every local dead-letter queue as empty and never peeks it. Unskip once the emulator reports counts or the adapter stops relying on them.
    private const string EmulatorReportsNoCounts = "The Service Bus emulator reports 0 for every runtime count (dead-letter, active and total).";

    [Fact(Skip = EmulatorReportsNoCounts)]
    public async Task DeadLetterQueuesAsync_CountsTheMessages_AndNamesWhenTheOldestWasEnqueued()
    {
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.CountsQueue, null, EmulatorMessages.New(NewId(), "first"), EmulatorMessages.New(NewId(), "second"));
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.CountsTopic, ServiceBusFixture.CountsSubscription, EmulatorMessages.New(NewId(), "only"));
        var subscription = $"{ServiceBusFixture.CountsTopic}/{ServiceBusFixture.CountsSubscription}";
        var queuePeek = await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.CountsQueue, null);

        var counted = await EmulatorMessages.EventuallyAsync(
            () => Adapter.DeadLetterQueuesAsync(Ct),
            queues => queues.Any(queue => queue.Name == ServiceBusFixture.CountsQueue && queue.Count == 2) && queues.Any(queue => queue.Name == subscription && queue.Count == 1));

        var queue = counted.Single(queue => queue.Name == ServiceBusFixture.CountsQueue);
        queue.Count.Should().Be(2);
        queue.OldestEnqueuedAt.Should().Be(queuePeek.Min(message => message.EnqueuedTime));
        queue.OldestEnqueuedAt.Should().BeBefore(DateTimeOffset.UtcNow);
        counted.Single(entity => entity.Name == subscription).Count.Should().Be(1);
        counted.Single(entity => entity.Name == ServiceBusFixture.EmptyQueue).Should().Be(new RecordedDeadLetters(ServiceBusFixture.EmptyQueue, 0, null));
    }

    [Fact]
    public async Task Looking_PeekingAndOpening_LeavesTheDeadLetterQueueUnchanged()
    {
        var correlationId = NewId();
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.PeekQueue, null, EmulatorMessages.New(correlationId, "one"), EmulatorMessages.New(correlationId, "two"));
        var before = await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.PeekQueue, null);

        var peeked = await Adapter.PeekAsync(ServiceBusFixture.PeekQueue, 10, Ct);
        var opened = await Adapter.OpenAsync(ServiceBusFixture.PeekQueue, before[0].SequenceNumber, Ct);
        await Adapter.DeadLetterQueuesAsync(Ct);

        peeked!.Select(message => message.SequenceNumber).Should().Equal(before.Select(message => message.SequenceNumber));
        peeked.Should().OnlyContain(message => message.Reason == "TestFailure" && message.CorrelationId == correlationId && message.BusinessKeys.Count == 1);
        opened!.Body.Should().Be("one");
        opened.Properties["colour"].Should().Be("green");
        var after = await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.PeekQueue, null);
        after.Select(message => (message.SequenceNumber, message.DeliveryCount, message.LockedUntil))
            .Should().Equal(before.Select(message => (message.SequenceNumber, message.DeliveryCount, message.LockedUntil)));
    }

    [Fact(Skip = EmulatorReportsNoCounts)]
    public async Task DeadLetteredAsync_FindsTheMessagesOfACorrelationId_WithoutChangingThem()
    {
        var correlationId = NewId();
        await EmulatorMessages.DeadLetterAsync(serviceBus.Client, ServiceBusFixture.PeekQueue, null, EmulatorMessages.New(correlationId, "one"), EmulatorMessages.New(correlationId, "two"));
        var before = await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.PeekQueue, null);

        var found = await Adapter.DeadLetteredAsync(correlationId, Ct);

        found.Select(message => message.Entity).Should().Equal(ServiceBusFixture.PeekQueue, ServiceBusFixture.PeekQueue);
        (await EmulatorMessages.PeekDeadLettersAsync(serviceBus.Client, ServiceBusFixture.PeekQueue, null)).Should().HaveCount(before.Count);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
