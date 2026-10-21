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
    private readonly List<ServiceBusMessage> _sent = [];

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

    [Fact]
    public async Task ResubmitAsync_ForAQueueMessage_SendsACopyToTheQueue_WithTheSamePropertiesAndANewMessageId()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Sender();
        Receives(Message(Enqueued, sequenceNumber: 3, reason: "MappingFailed", description: "Bad date", body: """{"consignee":"Café De Kroon"}""", correlationId: "corr-1",
            messageId: "original-1", subject: "TrackingEvent", contentType: "application/json", sessionId: "TR-7",
            properties: new Dictionary<string, object> { ["correlationId"] = "corr-1", ["shipmentNumber"] = "SH-0042", ["attempt"] = 3 }));

        await Queues().ResubmitAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        _client.Received(1).CreateSender("carrier-tracking");
        var sent = _sent.Should().ContainSingle().Subject;
        sent.Body.ToString().Should().Be("""{"consignee":"Café De Kroon"}""");
        sent.CorrelationId.Should().Be("corr-1");
        sent.Subject.Should().Be("TrackingEvent");
        sent.ContentType.Should().Be("application/json");
        sent.SessionId.Should().Be("TR-7");
        sent.MessageId.Should().NotBeNullOrEmpty().And.NotBe("original-1");
        sent.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["correlationId"] = "corr-1",
            ["shipmentNumber"] = "SH-0042",
            ["attempt"] = 3,
            ["resubmittedFrom"] = "original-1",
        });
    }

    [Fact]
    public async Task ResubmitAsync_WhenTheSendSucceeds_CompletesTheOriginal_AfterTheSend_AndReportsBothMessageIds()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var sender = Sender();
        var original = Message(Enqueued, sequenceNumber: 3, messageId: "original-1", correlationId: "corr-1",
            properties: new Dictionary<string, object> { ["shipmentNumber"] = "SH-0042" });
        var receiver = Receives(original);

        var resubmitted = await Queues().ResubmitAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        resubmitted.Should().NotBeNull();
        resubmitted!.Entity.Should().Be("carrier-tracking");
        resubmitted.OriginalMessageId.Should().Be("original-1");
        resubmitted.ResubmittedMessageId.Should().NotBeNullOrEmpty().And.NotBe("original-1").And.Be(_sent.Single().MessageId);
        resubmitted.CorrelationId.Should().Be("corr-1");
        resubmitted.BusinessKeys.Should().Equal(new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042"));
        Received.InOrder(() =>
        {
            sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
            receiver.CompleteMessageAsync(original, Arg.Any<CancellationToken>());
        });
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ResubmitAsync_WhenTheSendFails_LeavesTheOriginalOnTheDeadLetterQueue_AndFails()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var sender = Sender();
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ServiceBusException("Unavailable", ServiceBusFailureReason.ServiceCommunicationProblem));
        var original = Message(Enqueued, sequenceNumber: 3);
        var receiver = Receives(original);

        var resubmit = async () => await Queues().ResubmitAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        await resubmit.Should().ThrowAsync<ServiceBusException>();
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(original, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ResubmitAsync_WhenEarlierMessagesComeFirst_LetsGoOfThem_AndResubmitsOnlyTheOneWanted()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 3));
        Sender();
        var earlier = Message(Enqueued, sequenceNumber: 1, messageId: "earlier");
        var wanted = Message(Enqueued, sequenceNumber: 2, messageId: "wanted");
        var later = Message(Enqueued, sequenceNumber: 3, messageId: "later");
        var receiver = Receives(wanted, batches: [[earlier], [wanted, later]]);

        var resubmitted = await Queues().ResubmitAsync("carrier-tracking", 2, TestContext.Current.CancellationToken);

        resubmitted!.OriginalMessageId.Should().Be("wanted");
        await receiver.Received(1).CompleteMessageAsync(wanted, Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(earlier, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(later, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task ResubmitAsync_WhenTheMessageIsGone_ReturnsNull_AndSendsNothing()
    {
        // A peek from a sequence number gives the next message when that one is gone.
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Sender();
        var receiver = Receives(Message(Enqueued, sequenceNumber: 9));

        var resubmitted = await Queues().ResubmitAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        resubmitted.Should().BeNull();
        await receiver.DidNotReceive().ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ResubmitAsync_WhenAnotherOperatorTookItMeanwhile_ReturnsNull_AndSendsNothing()
    {
        // Peeked, then gone before the receive: the receive finds nothing.
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Sender();
        Receives(Message(Enqueued, sequenceNumber: 3), batches: []);

        var resubmitted = await Queues().ResubmitAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        resubmitted.Should().BeNull();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ResubmitAsync_ForAQueueTheEstateDoesNotHave_ReturnsNull_AndOpensNoClient()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));

        (await Queues().ResubmitAsync("nope", 3, TestContext.Current.CancellationToken)).Should().BeNull();

        _clients.DidNotReceiveWithAnyArgs().Create(default!);
    }

    [Fact]
    public async Task ResubmitAsync_ForASubscriptionMessage_SendsACopyToTheTopic_NamingTheSubscription_AndKeepsTheSessionAndCorrelation()
    {
        Found(new ServiceBusEntity("sb", "driver-app-events", "driver-reports", 1));
        var sender = Sender();
        var original = Message(Enqueued, sequenceNumber: 3, messageId: "original-1", correlationId: "corr-1", subject: "TripStartReported", contentType: "application/json", sessionId: "TR-7",
            properties: new Dictionary<string, object> { ["eventType"] = "TripStartReported", ["resubmitTo"] = "driver-reports" });
        var receiver = Receives(original);

        var resubmitted = await Queues().ResubmitAsync("driver-app-events/driver-reports", 3, TestContext.Current.CancellationToken);

        resubmitted!.Entity.Should().Be("driver-app-events/driver-reports");
        _client.Received(1).CreateSender("driver-app-events");
        var sent = _sent.Should().ContainSingle().Subject;
        sent.SessionId.Should().Be("TR-7");
        sent.Subject.Should().Be("TripStartReported");
        sent.ContentType.Should().Be("application/json");
        sent.CorrelationId.Should().Be("corr-1");
        sent.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "TripStartReported",
            ["resubmitTo"] = "driver-reports",
            ["resubmittedFrom"] = "original-1",
        });
        await receiver.Received(1).CompleteMessageAsync(original, Arg.Any<CancellationToken>());
        await sender.Received(1).SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResubmitAsync_ForAQueueMessage_NeverNamesASubscription()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Sender();
        Receives(Message(Enqueued, sequenceNumber: 3, properties: new Dictionary<string, object> { ["resubmitTo"] = "stale" }));

        await Queues().ResubmitAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        _sent.Single().ApplicationProperties.Should().NotContainKey("resubmitTo");
    }

    [Fact]
    public async Task DiscardAsync_ForAMessage_CompletesItOnTheDeadLetterQueue_SendsNothing_AndReportsIt()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        Sender();
        var original = Message(Enqueued, sequenceNumber: 3, messageId: "original-1", correlationId: "corr-1",
            properties: new Dictionary<string, object> { ["shipmentNumber"] = "SH-0042" });
        var receiver = Receives(original);

        var discarded = await Queues().DiscardAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        discarded.Should().NotBeNull();
        discarded!.Entity.Should().Be("carrier-tracking");
        discarded.MessageId.Should().Be("original-1");
        discarded.CorrelationId.Should().Be("corr-1");
        discarded.BusinessKeys.Should().Equal(new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042"));
        await receiver.Received(1).CompleteMessageAsync(original, Arg.Any<CancellationToken>());
        _sent.Should().BeEmpty();
        await _client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DiscardAsync_ForASubscriptionMessage_CompletesItOnThatSubscriptionsDeadLetterQueue()
    {
        Found(new ServiceBusEntity("sb", "driver-app-events", "driver-reports", 1));
        var original = Message(Enqueued, sequenceNumber: 3, messageId: "original-1");
        var receiver = Receives(original);

        var discarded = await Queues().DiscardAsync("driver-app-events/driver-reports", 3, TestContext.Current.CancellationToken);

        discarded!.Entity.Should().Be("driver-app-events/driver-reports");
        await receiver.Received(1).CompleteMessageAsync(original, Arg.Any<CancellationToken>());
        _client.Received(1).CreateReceiver("driver-app-events", "driver-reports", Arg.Any<ServiceBusReceiverOptions>());
    }

    [Fact]
    public async Task DiscardAsync_WhenTheCompletionFails_LeavesTheMessageOnTheDeadLetterQueue_AndFails()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var original = Message(Enqueued, sequenceNumber: 3);
        var receiver = Receives(original);
        receiver.CompleteMessageAsync(original, Arg.Any<CancellationToken>())
            .Returns(_ => throw new ServiceBusException("Lock lost", ServiceBusFailureReason.MessageLockLost));

        var discard = async () => await Queues().DiscardAsync("carrier-tracking", 3, TestContext.Current.CancellationToken);

        await discard.Should().ThrowAsync<ServiceBusException>();
        await receiver.Received(1).AbandonMessageAsync(original, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAsync_WhenEarlierMessagesComeFirst_LetsGoOfThem_AndDiscardsOnlyTheOneWanted()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 3));
        var earlier = Message(Enqueued, sequenceNumber: 1, messageId: "earlier");
        var wanted = Message(Enqueued, sequenceNumber: 2, messageId: "wanted");
        var receiver = Receives(wanted, batches: [[earlier, wanted]]);

        var discarded = await Queues().DiscardAsync("carrier-tracking", 2, TestContext.Current.CancellationToken);

        discarded!.MessageId.Should().Be("wanted");
        await receiver.Received(1).CompleteMessageAsync(wanted, Arg.Any<CancellationToken>());
        await receiver.Received(1).AbandonMessageAsync(earlier, Arg.Any<IDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAsync_WhenTheMessageIsGone_ReturnsNull_AndCompletesNothing()
    {
        // A peek from a sequence number gives the next message when that one is gone.
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var receiver = Receives(Message(Enqueued, sequenceNumber: 9));

        (await Queues().DiscardAsync("carrier-tracking", 3, TestContext.Current.CancellationToken)).Should().BeNull();

        await receiver.DidNotReceive().ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAsync_WhenAnotherOperatorTookItMeanwhile_ReturnsNull_AndCompletesNothing()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        var receiver = Receives(Message(Enqueued, sequenceNumber: 3), batches: []);

        (await Queues().DiscardAsync("carrier-tracking", 3, TestContext.Current.CancellationToken)).Should().BeNull();

        await receiver.DidNotReceive().CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardAsync_ForAQueueTheEstateDoesNotHave_ReturnsNull_AndOpensNoClient()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));

        (await Queues().DiscardAsync("nope", 3, TestContext.Current.CancellationToken)).Should().BeNull();

        _clients.DidNotReceive().Create(Arg.Any<string>());
    }

    [Fact]
    public async Task DeadLetteredAsync_ForACorrelationId_PeeksEveryNonEmptyDeadLetterQueue_ForTheMessagesCarryingIt()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 0), new ServiceBusEntity("sb", "shipment-events", "carrier-booking", 2));
        PeeksMany(
            Message(Enqueued, sequenceNumber: 4, correlationId: "corr-1", properties: new Dictionary<string, object> { ["shipmentNumber"] = "SH-0042" }),
            Message(Enqueued, sequenceNumber: 5, correlationId: "CORR-2"));

        var found = await Queues().DeadLetteredAsync("corr-1", TestContext.Current.CancellationToken);

        found.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FoundDeadLetter("shipment-events/carrier-booking", 4, Enqueued, [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042")]));
        _clients.Received(1).Create("sb");
    }

    [Fact]
    public async Task DeadLetteredAsync_ForACorrelationIdInDifferentCase_StillFindsTheMessage()
    {
        Found(new ServiceBusEntity("sb", "carrier-tracking", null, 1));
        PeeksMany(Message(Enqueued, correlationId: "Corr-1"));

        (await Queues().DeadLetteredAsync("corr-1", TestContext.Current.CancellationToken)).Should().ContainSingle();
    }

    private ServiceBusSender Sender()
    {
        var sender = Substitute.For<ServiceBusSender>();
        sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _client.CreateSender(Arg.Any<string>()).Returns(sender);
        return sender;
    }

    /// <summary>
    /// A dead-letter queue that peeks the message (or <paramref name="peeked"/> when given) and receives in order:
    /// <paramref name="batches"/> are what each receive returns, the message alone by default.
    /// </summary>
    private ServiceBusReceiver Receives(ServiceBusReceivedMessage message, IReadOnlyList<ServiceBusReceivedMessage>[]? batches = null)
    {
        var receiver = Peeks(message);
        var queue = new Queue<IReadOnlyList<ServiceBusReceivedMessage>>(batches ?? [[message]]);
        receiver.ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ => queue.Count > 0 ? queue.Dequeue() : []);
        return receiver;
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
        IDictionary<string, object>? properties = null,
        string? messageId = null,
        string? subject = null,
        string? contentType = null,
        string? sessionId = null)
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
            messageId: messageId,
            correlationId: correlationId,
            subject: subject,
            contentType: contentType,
            sessionId: sessionId,
            properties: applicationProperties,
            sequenceNumber: sequenceNumber,
            deliveryCount: deliveryCount,
            enqueuedTime: enqueued);
    }
}
