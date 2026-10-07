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
    [InlineData("not json", System.Net.HttpStatusCode.OK)]
    [InlineData("request", System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData("request", System.Net.HttpStatusCode.NotFound)]
    public async Task HandleAsync_ARunThatFails_StillCompletesTheMessage_AsTheTriggerDoes(string message, System.Net.HttpStatusCode tmsStatus)
    {
        _world.Tms.Otherwise(tmsStatus);

        await HandleAsync(message == "request" ? ShipperNotificationWorld.Request() : message);

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
        await _receiver.DidNotReceiveWithAnyArgs().DeadLetterMessageAsync(default!, default(string), default, TestContext.Current.CancellationToken);
    }
}
