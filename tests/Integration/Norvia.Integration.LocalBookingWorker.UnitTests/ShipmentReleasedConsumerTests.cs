using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalBookingWorker.UnitTests;

public class ShipmentReleasedConsumerTests
{
    private readonly BookingWorld _world = new();
    private readonly ServiceBusReceiver _receiver = Substitute.For<ServiceBusReceiver>();

    private async Task HandleAsync(string body)
    {
        var services = new ServiceCollection().AddSingleton(_world.Flow).BuildServiceProvider();
        var consumer = new ShipmentReleasedConsumer(
            Substitute.For<ServiceBusClient>(), services.GetRequiredService<IServiceScopeFactory>(), _world.Options, NullLogger<ShipmentReleasedConsumer>.Instance);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body));

        await consumer.HandleAsync(new ProcessMessageEventArgs(message, _receiver, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ABookingThatWasSentAndReported_CompletesTheMessage()
    {
        await HandleAsync(BookingWorld.Release());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not json", System.Net.HttpStatusCode.OK)]
    [InlineData("release", System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData("release", System.Net.HttpStatusCode.NotFound)]
    public async Task HandleAsync_ARunThatFails_StillCompletesTheMessage_AsTheTriggerDoes(string message, System.Net.HttpStatusCode tmsStatus)
    {
        _world.Tms.Otherwise(tmsStatus);

        await HandleAsync(message == "release" ? BookingWorld.Release() : message);

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().AbandonMessageAsync(default!, default, TestContext.Current.CancellationToken);
        await _receiver.DidNotReceiveWithAnyArgs().DeadLetterMessageAsync(default!, default(string), default, TestContext.Current.CancellationToken);
    }
}
