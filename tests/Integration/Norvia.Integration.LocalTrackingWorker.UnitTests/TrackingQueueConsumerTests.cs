using System.Net;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalTrackingWorker.UnitTests;

public class TrackingQueueConsumerTests
{
    private readonly TrackingWorld _world = new();
    private readonly ServiceBusReceiver _receiver = Substitute.For<ServiceBusReceiver>();

    private async Task HandleAsync(string body)
    {
        var services = new ServiceCollection().AddSingleton(_world.Flow).BuildServiceProvider();
        var consumer = new TrackingQueueConsumer(
            Substitute.For<ServiceBusClient>(), services.GetRequiredService<IServiceScopeFactory>(), _world.Options, _world.Time, NullLogger<TrackingQueueConsumer>.Instance);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString(body),
            properties: new Dictionary<string, object> { ["correlationId"] = "c-1", ["partyId"] = TrackingWorld.CarrierId, ["flow"] = "carrier-tracking" });

        await consumer.HandleAsync(new ProcessMessageEventArgs(message, _receiver, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ARecordedEvent_CompletesTheMessage()
    {
        await HandleAsync(TrackingWorld.Collected);

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_ARefusedEvent_DeadLettersItWithTheFailureCodeAndMessage()
    {
        _world.Tms.Otherwise(HttpStatusCode.NotFound, """{"code":"ShipmentNotFound","detail":"No such Shipment."}""");

        await HandleAsync(TrackingWorld.Collected);

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "ShipmentNotFound", "No such Shipment.", Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_AnUnmappableMessage_DeadLettersItAsUnmappable()
    {
        await HandleAsync("not json");

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "UnmappableTrackingMessage", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AnUnreachableTms_AbandonsTheMessage_ForTheQueueToDeliverItAgain()
    {
        _world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        await HandleAsync(TrackingWorld.Collected);

        await _receiver.Received(1).AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), cancellationToken: Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_AnUnreachableTms_WithARedeliveryDelay_HoldsTheMessageForTheDelay_BeforeAbandoningIt()
    {
        _world.Options.Value.RedeliveryDelay = TimeSpan.FromSeconds(2);
        _world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        var handling = HandleAsync(TrackingWorld.Collected);
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
        _world.Time.Advance(TimeSpan.FromSeconds(2));
        await handling;

        await _receiver.Received(1).AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), cancellationToken: Arg.Any<CancellationToken>());
    }
}
