using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;
using Norvia.IntegrationConsole.Infrastructure.DeadLetters;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.DeadLetters;

/// <summary>Locally the console lists what the Service Bus emulators hold, however many the AppHost runs.</summary>
public sealed class EmulatorServiceBusEntitiesTests
{
    [Fact]
    public async Task ListAsync_ForTwoEmulators_ListsTheQueuesAndSubscriptionsOfBoth_EachWithItsNamespace()
    {
        var messaging = Emulator(
            queues: [ServiceBusModelFactory.QueueRuntimeProperties("carrier-tracking", deadLetterMessageCount: 3)],
            topics: new() { ["shipment-events"] = [ServiceBusModelFactory.SubscriptionRuntimeProperties("shipment-events", "carrier-booking", deadLetterMessageCount: 1)] });
        var edge = Emulator(queues: [ServiceBusModelFactory.QueueRuntimeProperties("carrier-proof-of-delivery", deadLetterMessageCount: 0)]);
        var options = Options.Create(new LocalServiceBusOptions
        {
            Namespaces =
            {
                ["messaging-edge"] = new LocalServiceBusNamespace { ManagementConnectionString = "edge" },
                ["messaging"] = new LocalServiceBusNamespace { ManagementConnectionString = "main" },
            },
        });
        var entities = new EmulatorServiceBusEntities(options, connection => connection == "main" ? messaging : edge);

        var listed = await entities.ListAsync(TestContext.Current.CancellationToken);

        listed.Should().Equal(
            new ServiceBusEntity("messaging", "carrier-tracking", null, 3),
            new ServiceBusEntity("messaging", "shipment-events", "carrier-booking", 1),
            new ServiceBusEntity("messaging-edge", "carrier-proof-of-delivery", null, 0));
    }

    [Fact]
    public async Task ListAsync_WithNoEmulators_ListsNothing()
    {
        var entities = new EmulatorServiceBusEntities(Options.Create(new LocalServiceBusOptions()), _ => throw new InvalidOperationException());

        var listed = await entities.ListAsync(TestContext.Current.CancellationToken);

        listed.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithoutAnAdministrationClientFactory_ConnectsByTheManagementConnectionString()
    {
        var entities = new EmulatorServiceBusEntities(Options.Create(new LocalServiceBusOptions()));

        entities.Should().NotBeNull();
    }

    private static ServiceBusAdministrationClient Emulator(
        IReadOnlyList<QueueRuntimeProperties>? queues = null, Dictionary<string, IReadOnlyList<SubscriptionRuntimeProperties>>? topics = null)
    {
        var client = Substitute.For<ServiceBusAdministrationClient>();
        client.GetQueuesRuntimePropertiesAsync(Arg.Any<CancellationToken>()).Returns(Pages(queues ?? []));
        client.GetTopicsAsync(Arg.Any<CancellationToken>()).Returns(Pages((topics ?? []).Keys.Select(name => ServiceBusModelFactory.TopicProperties(name, defaultMessageTimeToLive: TimeSpan.FromDays(14), autoDeleteOnIdle: TimeSpan.FromDays(14), duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10))).ToList()));
        foreach (var (topic, subscriptions) in topics ?? [])
        {
            client.GetSubscriptionsRuntimePropertiesAsync(topic, Arg.Any<CancellationToken>()).Returns(Pages(subscriptions));
        }

        return client;
    }

    private static AsyncPageable<T> Pages<T>(IReadOnlyList<T> items)
        where T : notnull =>
        AsyncPageable<T>.FromPages([Page<T>.FromValues(items, null, Substitute.For<Response>())]);
}
