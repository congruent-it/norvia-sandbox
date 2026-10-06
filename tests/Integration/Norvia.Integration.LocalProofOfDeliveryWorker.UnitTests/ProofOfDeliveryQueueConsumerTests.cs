using System.Net;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalProofOfDeliveryWorker.UnitTests;

public class ProofOfDeliveryQueueConsumerTests
{
    private readonly ProofOfDeliveryWorld _world = new();
    private readonly ServiceBusReceiver _receiver = Substitute.For<ServiceBusReceiver>();

    private async Task HandleAsync(string body)
    {
        var services = new ServiceCollection().AddSingleton(_world.Flow).BuildServiceProvider();
        var consumer = new ProofOfDeliveryQueueConsumer(
            Substitute.For<ServiceBusClient>(), services.GetRequiredService<IServiceScopeFactory>(), _world.Options, NullLogger<ProofOfDeliveryQueueConsumer>.Instance);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString(body),
            properties: new Dictionary<string, object> { ["correlationId"] = "c-1", ["partyId"] = ProofOfDeliveryWorld.CarrierId, ["flow"] = "carrier-proof-of-delivery" });

        await consumer.HandleAsync(new ProcessMessageEventArgs(message, _receiver, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ARecordedProof_CompletesTheMessage()
    {
        await HandleAsync(ProofOfDeliveryWorld.Reference());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_ARefusedProof_DeadLettersItWithTheFailureCodeAndMessage()
    {
        _world.Tms.Otherwise(HttpStatusCode.Conflict, """{"code":"ShipmentNotInTransport","detail":"Shipment SH-1 is Planned."}""");

        await HandleAsync(ProofOfDeliveryWorld.Reference());

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "ShipmentNotInTransport", "Shipment SH-1 is Planned.", Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_AMissingBlob_DeadLettersItAsMissing()
    {
        await HandleAsync(ProofOfDeliveryWorld.Reference("no-such-blob"));

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "ProofOfDeliveryBlobMissing", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AnUnreachableTms_AbandonsTheMessage_ForTheQueueToDeliverItAgain()
    {
        _world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable);

        await HandleAsync(ProofOfDeliveryWorld.Reference());

        await _receiver.Received(1).AbandonMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), cancellationToken: Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }
}
