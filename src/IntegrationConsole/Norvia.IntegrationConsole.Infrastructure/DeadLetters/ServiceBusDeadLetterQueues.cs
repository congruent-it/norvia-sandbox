using Azure.Messaging.ServiceBus;
using Norvia.IntegrationConsole.Application.DeadLetters;

namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>
/// Counts the dead-lettered messages of every queue and subscription the estate has, and peeks the oldest of each dead-letter
/// queue that holds any, to see how long it has waited. A peek takes nothing off the queue, and no message is received,
/// completed or sent.
/// </summary>
internal sealed class ServiceBusDeadLetterQueues(IServiceBusEntities entities, IServiceBusClients clients) : IDeadLetterQueues
{
    public async Task<IReadOnlyList<RecordedDeadLetters>> DeadLetterQueuesAsync(CancellationToken cancellationToken)
    {
        var found = await entities.ListAsync(cancellationToken);
        var opened = new Dictionary<string, ServiceBusClient>(StringComparer.Ordinal);
        try
        {
            var recorded = new List<RecordedDeadLetters>(found.Count);
            foreach (var entity in found)
            {
                DateTimeOffset? oldest = null;
                if (entity.DeadLetterCount > 0)
                {
                    if (!opened.TryGetValue(entity.Namespace, out var client))
                    {
                        opened[entity.Namespace] = client = clients.Create(entity.Namespace);
                    }

                    oldest = await OldestAsync(client, entity, cancellationToken);
                }

                recorded.Add(new RecordedDeadLetters(entity.Name, entity.DeadLetterCount, oldest));
            }

            return recorded;
        }
        finally
        {
            foreach (var client in opened.Values)
            {
                await client.DisposeAsync();
            }
        }
    }

    private static async Task<DateTimeOffset?> OldestAsync(ServiceBusClient client, ServiceBusEntity entity, CancellationToken cancellationToken)
    {
        var options = new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter };
        await using var receiver = entity.Subscription is null
            ? client.CreateReceiver(entity.Queue, options)
            : client.CreateReceiver(entity.Queue, entity.Subscription, options);
        // The lowest sequence number is the one dead-lettered first.
        return (await receiver.PeekMessageAsync(cancellationToken: cancellationToken))?.EnqueuedTime;
    }
}
