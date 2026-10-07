using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalShipperNotificationWorker.UnitTests;

public class ShipperNotificationRequestedConsumerTests
{
    private readonly ShipperNotificationWorld _world = new();
    private readonly ServiceBusReceiver _receiver = Substitute.For<ServiceBusReceiver>();

    private async Task HandleAsync(string body)
    {
        var services = new ServiceCollection().AddSingleton(_world.Flow).BuildServiceProvider();
        var consumer = new ShipperNotificationRequestedConsumer(
            Substitute.For<ServiceBusClient>(), services.GetRequiredService<IServiceScopeFactory>(), _world.Options, NullLogger<ShipperNotificationRequestedConsumer>.Instance);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body));

        await consumer.HandleAsync(new ProcessMessageEventArgs(message, _receiver, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ANotificationThatWasSentAndReported_CompletesTheMessage()
    {
        await HandleAsync(ShipperNotificationWorld.Request());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.NotFound)]
    public async Task HandleAsync_ARunThatFailsToReport_StillCompletesTheMessage_AsTheWorkflowDoes(System.Net.HttpStatusCode tmsStatus)
    {
        _world.Tms.Otherwise(tmsStatus);

        await HandleAsync(ShipperNotificationWorld.Request());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
        await _receiver.DidNotReceiveWithAnyArgs().DeadLetterMessageAsync(default!, default(string), default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_AnUnreadableMessage_IsDeadLetteredWithTheFailureCode_AndNotCompleted()
    {
        await HandleAsync("not json");

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "ShipperNotificationRequestedUnreadable", Arg.Is<string>(text => text.Contains("nothing was sent")), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }
}
