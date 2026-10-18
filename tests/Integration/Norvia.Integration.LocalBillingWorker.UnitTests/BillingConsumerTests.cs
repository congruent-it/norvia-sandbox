using System.Net;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalBillingWorker.UnitTests;

public class BillingConsumerTests
{
    private readonly BillingWorld _world = new();
    private readonly ServiceBusReceiver _receiver = Substitute.For<ServiceBusReceiver>();

    private async Task HandleAsync(string body)
    {
        var services = new ServiceCollection().AddSingleton(_world.Flow).BuildServiceProvider();
        var consumer = new BillingConsumer(
            Substitute.For<ServiceBusClient>(), services.GetRequiredService<IServiceScopeFactory>(), _world.Options, NullLogger<BillingConsumer>.Instance);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body));

        await consumer.HandleAsync(new ProcessMessageEventArgs(message, _receiver, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ALineTheErpRecorded_CompletesTheMessage()
    {
        await HandleAsync(BillingWorld.Billable());

        await _receiver.Received(1).CompleteMessageAsync(Arg.Any<ServiceBusReceivedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AConflict_IsDeadLetteredWithTheErpsCodeAndMessage_AndNotCompleted()
    {
        _world.Erp.Otherwise(HttpStatusCode.Conflict, """{"detail":"Already recorded differently.","code":"BillableLineConflict"}""");

        await HandleAsync(BillingWorld.Billable());

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "BillableLineConflict", "Already recorded differently.", Arg.Any<CancellationToken>());
        await _receiver.DidNotReceiveWithAnyArgs().CompleteMessageAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_AnUnreadableMessage_IsDeadLetteredWithTheFailureCode()
    {
        await HandleAsync("not json");

        await _receiver.Received(1).DeadLetterMessageAsync(
            Arg.Any<ServiceBusReceivedMessage>(), "BillableLineUnreadable", Arg.Is<string>(text => text.Contains("nothing was sent")), Arg.Any<CancellationToken>());
    }
}
