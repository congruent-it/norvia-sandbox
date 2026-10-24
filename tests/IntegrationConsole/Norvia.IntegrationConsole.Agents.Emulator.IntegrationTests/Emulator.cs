using Azure.Messaging.ServiceBus;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>What the tests do to the emulator directly, as the apps would: send, dead-letter and look.</summary>
internal static class Emulator
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>A message with the tracking contract's keys, a body of its own and a custom property.</summary>
    public static ServiceBusMessage Message(string correlationId, string body) =>
        new(body)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId,
            ContentType = "application/json",
            Subject = "TrackingEvent",
            ApplicationProperties = { ["correlationId"] = correlationId, ["shipmentNumber"] = "SH-1000001", ["colour"] = "green" },
        };

    /// <summary>Sends the messages to a queue or topic, takes them off the queue or subscription and dead-letters them in the order sent.</summary>
    public static async Task DeadLetterAsync(ServiceBusClient client, string queueOrTopic, string? subscription, string reason, params ServiceBusMessage[] messages)
    {
        await using (var sender = client.CreateSender(queueOrTopic))
        {
            foreach (var message in messages)
            {
                await sender.SendMessageAsync(message, TestContext.Current.CancellationToken);
            }
        }

        await using var receiver = Receiver(client, queueOrTopic, subscription);
        for (var received = 0; received < messages.Length; received++)
        {
            var message = await receiver.ReceiveMessageAsync(Patience, TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException($"Message {received + 1} of {messages.Length} never reached {queueOrTopic}.");
            await receiver.DeadLetterMessageAsync(message, reason, $"The test dead-lettered it with {reason}.", TestContext.Current.CancellationToken);
        }
    }

    /// <summary>The messages of a dead-letter queue as the test peeks them itself, to compare with what the console shows.</summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekDeadLettersAsync(ServiceBusClient client, string queueOrTopic, string? subscription)
    {
        await using var receiver = Receiver(client, queueOrTopic, subscription, SubQueue.DeadLetter);
        return await receiver.PeekMessagesAsync(100, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Receives the messages of a dead-letter queue, which a lock on any of them would stop, and abandons them again.</summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveDeadLettersAsync(ServiceBusClient client, string queueOrTopic, string? subscription, int count)
    {
        await using var receiver = Receiver(client, queueOrTopic, subscription, SubQueue.DeadLetter);
        var received = await receiver.ReceiveMessagesAsync(count, Patience, TestContext.Current.CancellationToken);
        foreach (var message in received)
        {
            await receiver.AbandonMessageAsync(message, cancellationToken: TestContext.Current.CancellationToken);
        }

        return received;
    }

    /// <summary>Takes the messages that arrive on a queue or subscription within <paramref name="wait"/> and completes them.</summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> TakeAsync(ServiceBusClient client, string queueOrTopic, string? subscription, int count, TimeSpan? wait = null)
    {
        await using var receiver = Receiver(client, queueOrTopic, subscription);
        var taken = new List<ServiceBusReceivedMessage>();
        while (taken.Count < count)
        {
            var batch = await receiver.ReceiveMessagesAsync(count - taken.Count, wait ?? Patience, TestContext.Current.CancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var message in batch)
            {
                await receiver.CompleteMessageAsync(message, TestContext.Current.CancellationToken);
                taken.Add(message);
            }
        }

        return taken;
    }

    /// <summary>Polls until the condition holds: the emulator's runtime counts trail what it has just done.</summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Patience);
        while (true)
        {
            var value = await read();
            if (done(value) || timeout.IsCancellationRequested)
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }
    }

    private static ServiceBusReceiver Receiver(ServiceBusClient client, string queueOrTopic, string? subscription, SubQueue subQueue = SubQueue.None)
    {
        var options = new ServiceBusReceiverOptions { SubQueue = subQueue };
        return subscription is null ? client.CreateReceiver(queueOrTopic, options) : client.CreateReceiver(queueOrTopic, subscription, options);
    }
}
