using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;

namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>Lists the entities of the Service Bus emulators the AppHost runs, through their management port.</summary>
internal sealed class EmulatorServiceBusEntities(IOptions<LocalServiceBusOptions> options, Func<string, ServiceBusAdministrationClient>? administration = null)
    : IServiceBusEntities
{
    private readonly Func<string, ServiceBusAdministrationClient> _administration =
        administration ?? (connectionString => new ServiceBusAdministrationClient(connectionString));

    public async Task<IReadOnlyList<ServiceBusEntity>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = new List<ServiceBusEntity>();
        foreach (var (name, local) in options.Value.Namespaces.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var client = _administration(local.ManagementConnectionString);
            await foreach (var queue in client.GetQueuesRuntimePropertiesAsync(cancellationToken))
            {
                entities.Add(new ServiceBusEntity(name, queue.Name, null, queue.DeadLetterMessageCount));
            }

            await foreach (var topic in client.GetTopicsAsync(cancellationToken))
            {
                await foreach (var subscription in client.GetSubscriptionsRuntimePropertiesAsync(topic.Name, cancellationToken))
                {
                    entities.Add(new ServiceBusEntity(name, topic.Name, subscription.SubscriptionName, subscription.DeadLetterMessageCount));
                }
            }
        }

        return entities;
    }
}
