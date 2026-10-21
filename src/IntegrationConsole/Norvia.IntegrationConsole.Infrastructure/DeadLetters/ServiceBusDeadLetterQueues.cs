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
/// Resubmitting is the one thing that receives: it sends a copy to the queue, or to the topic for one subscription, and only then completes the original.
/// </summary>
internal sealed class ServiceBusDeadLetterQueues(IServiceBusEntities entities, IServiceBusClients clients) : IDeadLetterQueues
{
    /// <summary>The application property a resubmitted copy names the original's message ID in.</summary>
    public const string ResubmittedFrom = "resubmittedFrom";

    /// <summary>
    /// The application property a copy for a subscription names that subscription in. Every subscription's filter takes a
    /// message that carries its own name here and none that carries another's (coding guidelines §7).
    /// </summary>
    public const string ResubmitTo = "resubmitTo";

    private const int ReceiveBatchSize = 10;
    private static readonly TimeSpan ReceiveWait = TimeSpan.FromSeconds(2);

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

    public async Task<ResubmittedDeadLetter?> ResubmitAsync(string entity, long sequenceNumber, CancellationToken cancellationToken)
    {
        if (await FindAsync(entity, cancellationToken) is not { } found)
        {
            return null;
        }

        await using var client = clients.Create(found.Namespace);
        await using var receiver = DeadLetterReceiver(client, found);
        // Peeked first, so a message that is gone doesn't lock the ones before it.
        if (await receiver.PeekMessageAsync(sequenceNumber, cancellationToken) is not { } peeked || peeked.SequenceNumber != sequenceNumber
            || await ReceiveAsync(receiver, sequenceNumber, cancellationToken) is not { } original)
        {
            return null;
        }

        // A subscription's message goes to its topic, for only that subscription to take; a queue's goes to the queue.
        var copy = Copy(original, found.Subscription);
        await using var sender = client.CreateSender(found.Queue);
        try
        {
            await sender.SendMessageAsync(copy, cancellationToken);
        }
        catch
        {
            // Left on the dead-letter queue, and unlocked so the next try doesn't wait for the lock to expire.
            await receiver.AbandonMessageAsync(original, cancellationToken: CancellationToken.None);
            throw;
        }

        await receiver.CompleteMessageAsync(original, cancellationToken);
        var listed = Listed(original);
        return new ResubmittedDeadLetter(found.Name, original.MessageId, copy.MessageId, listed.CorrelationId, listed.BusinessKeys);
    }

    /// <summary>
    /// A dead-letter queue can't be received from by sequence number, so it receives in order and lets go of the messages
    /// before the one wanted. Null when it isn't there any more.
    /// </summary>
    private static async Task<ServiceBusReceivedMessage?> ReceiveAsync(ServiceBusReceiver receiver, long sequenceNumber, CancellationToken cancellationToken)
    {
        while (true)
        {
            var batch = await receiver.ReceiveMessagesAsync(ReceiveBatchSize, ReceiveWait, cancellationToken);
            if (batch.Count == 0)
            {
                return null;
            }

            ServiceBusReceivedMessage? wanted = null;
            foreach (var message in batch)
            {
                if (message.SequenceNumber == sequenceNumber)
                {
                    wanted = message;
                }
                else
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken);
                }
            }

            if (wanted is not null)
            {
                return wanted;
            }
        }
    }

    private static ServiceBusMessage Copy(ServiceBusReceivedMessage original, string? subscription)
    {
        var copy = new ServiceBusMessage(original.Body)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = original.CorrelationId,
            ContentType = original.ContentType,
            Subject = original.Subject,
            SessionId = original.SessionId,
        };
        foreach (var (name, value) in original.ApplicationProperties)
        {
            // The dead-letter reason and description are how it failed, not what it carried.
            if (name is not ("DeadLetterReason" or "DeadLetterErrorDescription" or ResubmitTo))
            {
                copy.ApplicationProperties[name] = value;
            }
        }

        copy.ApplicationProperties[ResubmittedFrom] = original.MessageId;
        if (subscription is not null)
        {
            copy.ApplicationProperties[ResubmitTo] = subscription;
        }

        return copy;
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
