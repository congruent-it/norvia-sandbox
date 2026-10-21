using Azure.Messaging.ServiceBus;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Infrastructure.DeadLetters;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.DeadLetters;

/// <summary>The console counts dead-lettered messages and peeks the oldest of each dead-letter queue, and takes nothing off it.</summary>
public sealed class ServiceBusDeadLetterQueuesTests
{
    private static readonly DateTimeOffset Enqueued = new(2026, 9, 26, 8, 30, 0, TimeSpan.Zero);

    private readonly IServiceBusEntities _entities = Substitute.For<IServiceBusEntities>();
    private readonly IServiceBusClients _clients = Substitute.For<IServiceBusClients>();
    private readonly ServiceBusClient _client = Substitute.For<ServiceBusClient>();

    public ServiceBusDeadLetterQueuesTests() => _clients.Create(Arg.Any<string>()).Returns(_client);

    [Fact]
    public async Task DeadLetterQueuesAsync_ForAQueueWithMessages_PeeksItsDeadLetterQueue_ForTheOldestOnesEnqueuedTime()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 3));
        var receiver = Peeks(Message(Enqueued));

        var recorded = await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        recorded.Should().Equal(new RecordedDeadLetters("carrier-tracking", 3, Enqueued));
        _client.Received(1).CreateReceiver("carrier-tracking", Arg.Is<ServiceBusReceiverOptions>(options => options.SubQueue == SubQueue.DeadLetter));
        await receiver.Received(1).PeekMessageAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeadLetterQueuesAsync_ForASubscriptionWithMessages_PeeksItsDeadLetterQueue_NamedByTopicAndSubscription()
    {
        Found(new ServiceBusEntity("sb", "shipment-events", "carrier-booking", 1));
        var receiver = Peeks(Message(Enqueued));
        _client.CreateReceiver("shipment-events", "carrier-booking", Arg.Any<ServiceBusReceiverOptions>()).Returns(receiver);

        var recorded = await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        recorded.Should().Equal(new RecordedDeadLetters("shipment-events/carrier-booking", 1, Enqueued));
        _client.Received(1).CreateReceiver("shipment-events", "carrier-booking", Arg.Is<ServiceBusReceiverOptions>(options => options.SubQueue == SubQueue.DeadLetter));
    }

    [Fact]
    public async Task DeadLetterQueuesAsync_ForAQueueWithNoMessages_OpensNoClientAndPeeksNothing()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 0));

        var recorded = await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        recorded.Should().Equal(new RecordedDeadLetters("carrier-tracking", 0, null));
        _clients.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Fact]
    public async Task DeadLetterQueuesAsync_WhenThePeekFindsNothing_RecordsTheCountWithoutAnAge()
    {
        // A message may have been resubmitted between the count and the peek.
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Peeks(null);

        var recorded = await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        recorded.Should().Equal(new RecordedDeadLetters("carrier-tracking", 1, null));
    }

    [Fact]
    public async Task DeadLetterQueuesAsync_ForSeveralEntitiesOfOneNamespace_OpensOneClient_AndDisposesItAtTheEnd()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1), new ServiceBusEntity("sb", "carrier-proof-of-delivery", null, 1));
        Peeks(Message(Enqueued));

        await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        _clients.Received(1).Create("sb");
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DeadLetterQueuesAsync_ForEntitiesOfTwoNamespaces_OpensAClientForEach()
    {
        Found(new ServiceBusEntity("messaging", "transport-order-events", "billing", 1), new ServiceBusEntity("messaging-edge", "carrier-tracking", null, 1));
        Peeks(Message(Enqueued));

        await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        _clients.Received(1).Create("messaging");
        _clients.Received(1).Create("messaging-edge");
    }

    [Fact]
    public async Task DeadLetterQueuesAsync_WhenAPeekFails_DisposesTheClient_AndFails()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var receiver = Substitute.For<ServiceBusReceiver>();
        receiver.PeekMessageAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns<ServiceBusReceivedMessage?>(_ => throw new UnauthorizedAccessException());
        _client.CreateReceiver(Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>()).Returns(receiver);

        var list = async () => await Queues().DeadLetterQueuesAsync(TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<UnauthorizedAccessException>();
        await _client.Received(1).DisposeAsync();
    }

    private ServiceBusDeadLetterQueues Queues() => new(_entities, _clients);

    private void Found(params ServiceBusEntity[] found) => _entities.ListAsync(Arg.Any<CancellationToken>()).Returns(found);

    private ServiceBusReceiver Peeks(ServiceBusReceivedMessage? message)
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        receiver.PeekMessageAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(message);
        _client.CreateReceiver(Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>()).Returns(receiver);
        _client.CreateReceiver(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>()).Returns(receiver);
        return receiver;
    }

    private static ServiceBusReceivedMessage Message(DateTimeOffset enqueued) => ServiceBusModelFactory.ServiceBusReceivedMessage(enqueuedTime: enqueued);
}
