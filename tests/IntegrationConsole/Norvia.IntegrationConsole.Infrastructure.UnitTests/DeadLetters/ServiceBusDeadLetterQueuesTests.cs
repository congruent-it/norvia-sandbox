using Azure.Messaging.ServiceBus;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
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

    [Fact]
    public async Task PeekAsync_ForAQueue_PeeksItsDeadLetterQueue_AndReceivesNothing()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 2));
        var receiver = PeeksMany(
            Message(Enqueued, sequenceNumber: 3, reason: "MaxDeliveryCountExceeded", description: "Failed 10 times", deliveryCount: 10),
            Message(Enqueued.AddMinutes(1), sequenceNumber: 4));

        var peeked = await Queues().PeekAsync("carrier-tracking", 101, TestContext.Current.CancellationToken);

        peeked.Should().NotBeNull();
        peeked!.Select(message => message.SequenceNumber).Should().Equal(3, 4);
        peeked[0].Should().BeEquivalentTo(new { Reason = "MaxDeliveryCountExceeded", Description = "Failed 10 times", DeliveryCount = 10, EnqueuedAt = Enqueued });
        _client.Received(1).CreateReceiver("carrier-tracking", Arg.Is<ServiceBusReceiverOptions>(options => options.SubQueue == SubQueue.DeadLetter));
        await receiver.Received(1).PeekMessagesAsync(101, null, Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().DeadLetterMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task PeekAsync_ForASubscription_PeeksItsDeadLetterQueue()
    {
        Found(new ServiceBusEntity("sb", "shipment-events", "carrier-booking", 1));
        PeeksMany(Message(Enqueued, sequenceNumber: 1));

        var peeked = await Queues().PeekAsync("shipment-events/carrier-booking", 10, TestContext.Current.CancellationToken);

        peeked.Should().ContainSingle();
        _client.Received(1).CreateReceiver("shipment-events", "carrier-booking", Arg.Is<ServiceBusReceiverOptions>(options => options.SubQueue == SubQueue.DeadLetter));
    }

    [Fact]
    public async Task PeekAsync_Always_ListsTheTrackingContractsKeys_AndNothingOfTheBody()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        PeeksMany(Message(Enqueued, sequenceNumber: 3, body: "Café De Kroon", properties: new Dictionary<string, object>
        {
            ["correlationId"] = "corr-1",
            ["shipmentNumber"] = "SH-0042",
            ["tripNumber"] = "",
            ["flow"] = "carrier-tracking",
        }));

        var peeked = await Queues().PeekAsync("carrier-tracking", 10, TestContext.Current.CancellationToken);

        var message = peeked!.Should().ContainSingle().Subject;
        message.CorrelationId.Should().Be("corr-1");
        message.BusinessKeys.Should().Equal(new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042"));
        message.ToString().Should().NotContain("Café");
    }

    [Fact]
    public async Task PeekAsync_WithoutATrackedCorrelationId_FallsBackToTheMessagesOwn()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        PeeksMany(Message(Enqueued, sequenceNumber: 3, correlationId: "corr-2"));

        var peeked = await Queues().PeekAsync("carrier-tracking", 10, TestContext.Current.CancellationToken);

        peeked!.Single().CorrelationId.Should().Be("corr-2");
    }

    [Fact]
    public async Task PeekAsync_ForAQueueTheEstateDoesNotHave_ReturnsNull_AndOpensNoClient()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));

        var peeked = await Queues().PeekAsync("nope", 10, TestContext.Current.CancellationToken);

        peeked.Should().BeNull();
        _clients.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Fact]
    public async Task OpenAsync_ForAMessage_PeeksItBySequenceNumber_AndReturnsItsBodyAndApplicationProperties()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var receiver = Peeks(Message(Enqueued, sequenceNumber: 3, body: """{"consignee":"Café De Kroon"}""", properties: new Dictionary<string, object>
        {
            ["shipmentNumber"] = "SH-0042",
            ["attempt"] = 3,
        }));

        var opened = await Queues().OpenAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        opened.Should().NotBeNull();
        opened!.Body.Should().Be("""{"consignee":"Café De Kroon"}""");
        opened.Properties.Should().BeEquivalentTo(new Dictionary<string, string> { ["shipmentNumber"] = "SH-0042", ["attempt"] = "3" });
        opened.Message.SequenceNumber.Should().Be(3);
        await receiver.Received(1).PeekMessageAsync(3, Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_WhenTheMessageIsGone_ReturnsNull_ForThePeekGivesTheNextOne()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Peeks(Message(Enqueued, sequenceNumber: 9));

        var opened = await Queues().OpenAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        opened.Should().BeNull();
    }

    [Fact]
    public async Task OpenAsync_WhenTheQueueIsEmpty_ReturnsNull()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 0));
        Peeks(null);

        (await Queues().OpenAsync("carrier-tracking", 3, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task OpenAsync_ForAQueueTheEstateDoesNotHave_ReturnsNull()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));

        (await Queues().OpenAsync("nope", 3, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    private ServiceBusReceiver PeeksMany(params ServiceBusReceivedMessage[] messages)
    {
        var receiver = Substitute.For<ServiceBusReceiver>();
        receiver.PeekMessagesAsync(Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>()).Returns(messages);
        _client.CreateReceiver(Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>()).Returns(receiver);
        _client.CreateReceiver(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>()).Returns(receiver);
        return receiver;
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

    private static ServiceBusReceivedMessage Message(
        DateTimeOffset enqueued,
        long sequenceNumber = 1,
        string? reason = null,
        string? description = null,
        int deliveryCount = 1,
        string body = "{}",
        string? correlationId = null,
        IDictionary<string, object>? properties = null)
    {
        var applicationProperties = properties ?? new Dictionary<string, object>();
        if (reason is not null)
        {
            applicationProperties["DeadLetterReason"] = reason;
        }

        if (description is not null)
        {
            applicationProperties["DeadLetterErrorDescription"] = description;
        }

        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body),
            correlationId: correlationId,
            properties: applicationProperties,
            sequenceNumber: sequenceNumber,
            deliveryCount: deliveryCount,
            enqueuedTime: enqueued);
    }
}
