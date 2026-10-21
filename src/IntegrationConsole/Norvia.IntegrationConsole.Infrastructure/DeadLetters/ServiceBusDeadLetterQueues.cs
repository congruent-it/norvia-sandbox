using System.Globalization;
using Azure.Messaging.ServiceBus;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.DeadLetters;

/// <summary>
/// Counts the dead-lettered messages of every queue and subscription the estate has, and peeks the oldest of each dead-letter
/// queue that holds any, to see how long it has waited. It also peeks a queue's messages, and one message in full. A peek
/// takes nothing off the queue, and no message is received, completed or sent: it keeps its delivery count and lock state.
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

    public async Task<IReadOnlyList<RecordedDeadLetter>?> PeekAsync(string entity, int maxMessages, CancellationToken cancellationToken)
    {
        if (await FindAsync(entity, cancellationToken) is not { } found)
        {
            return null;
        }

        await using var client = clients.Create(found.Namespace);
        await using var receiver = DeadLetterReceiver(client, found);
        var peeked = await receiver.PeekMessagesAsync(maxMessages, cancellationToken: cancellationToken);
        return [.. peeked.Select(Listed)];
    }

    public async Task<RecordedDeadLetterBody?> OpenAsync(string entity, long sequenceNumber, CancellationToken cancellationToken)
    {
        if (await FindAsync(entity, cancellationToken) is not { } found)
        {
            return null;
        }

        await using var client = clients.Create(found.Namespace);
        await using var receiver = DeadLetterReceiver(client, found);
        // A peek from a sequence number returns the first message at or after it, so one that is gone gives the next.
        if (await receiver.PeekMessageAsync(sequenceNumber, cancellationToken) is not { } message || message.SequenceNumber != sequenceNumber)
        {
            return null;
        }

        var properties = message.ApplicationProperties.ToDictionary(
            property => property.Key, property => Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparer.Ordinal);
        return new RecordedDeadLetterBody(Listed(message), message.Body.ToString(), properties);
    }

    private async Task<ServiceBusEntity?> FindAsync(string entity, CancellationToken cancellationToken) =>
        (await entities.ListAsync(cancellationToken)).FirstOrDefault(found => string.Equals(found.Name, entity, StringComparison.Ordinal));

    private static RecordedDeadLetter Listed(ServiceBusReceivedMessage message)
    {
        var properties = message.ApplicationProperties;
        var keys = TrackedBusinessKeys.All
            .Where(tracked => properties.TryGetValue(tracked.Key, out var value) && !string.IsNullOrEmpty(Convert.ToString(value, CultureInfo.InvariantCulture)))
            .Select(tracked => new BusinessKey(tracked.Kind, Convert.ToString(properties[tracked.Key], CultureInfo.InvariantCulture)!))
            .ToList();
        var tracking = properties.TryGetValue("correlationId", out var correlationId) ? Convert.ToString(correlationId, CultureInfo.InvariantCulture) : null;
        return new RecordedDeadLetter(
            message.SequenceNumber,
            message.EnqueuedTime,
            message.DeadLetterReason,
            message.DeadLetterErrorDescription,
            message.DeliveryCount,
            string.IsNullOrEmpty(tracking) ? message.CorrelationId : tracking,
            keys);
    }

    private static ServiceBusReceiver DeadLetterReceiver(ServiceBusClient client, ServiceBusEntity entity)
    {
        var options = new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter };
        return entity.Subscription is null
            ? client.CreateReceiver(entity.Queue, options)
            : client.CreateReceiver(entity.Queue, entity.Subscription, options);
    }

    private static async Task<DateTimeOffset?> OldestAsync(ServiceBusClient client, ServiceBusEntity entity, CancellationToken cancellationToken)
    {
        await using var receiver = DeadLetterReceiver(client, entity);
        // The lowest sequence number is the one dead-lettered first.
        return (await receiver.PeekMessageAsync(cancellationToken: cancellationToken))?.EnqueuedTime;
    }
}
