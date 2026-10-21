using Azure.Messaging.ServiceBus;

namespace Norvia.IntegrationConsole.Infrastructure.IntegrationTests;

/// <summary>What the tests do to the emulator directly, as the apps would: send, dead-letter, receive and look.</summary>
internal static class EmulatorMessages
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>A message with the tracking contract's keys, a body of its own and a custom property.</summary>
    public static ServiceBusMessage New(string correlationId, string body, string? sessionId = null) =>
        new(body)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId,
            ContentType = "application/json",
            Subject = "ShipmentEvent",
            SessionId = sessionId,
            ApplicationProperties = { ["correlationId"] = correlationId, ["shipmentNumber"] = "SH-1000001", ["colour"] = "green" },
        };

    /// <summary>Sends the messages to a queue or topic, takes them off the queue or subscription and dead-letters them in the order sent.</summary>
    public static async Task DeadLetterAsync(ServiceBusClient client, string queueOrTopic, string? subscription, params ServiceBusMessage[] messages)
    {
        await using (var sender = client.CreateSender(queueOrTopic))
        {
            foreach (var message in messages)
            {
                await sender.SendMessageAsync(message, TestContext.Current.CancellationToken);
            }
        }

        var sessions = messages.Select(message => message.SessionId).Distinct().ToList();
        var bySession = sessions.Count == 1 && sessions[0] is null ? [(null as string, messages.Length)] : sessions.Select(id => (id, messages.Count(message => message.SessionId == id))).ToList();
        foreach (var (sessionId, count) in bySession)
        {
            await using var receiver = sessionId is null
                ? Receiver(client, queueOrTopic, subscription)
                : await AcceptSessionAsync(client, queueOrTopic, subscription, sessionId);
            for (var received = 0; received < count; received++)
            {
                var message = await receiver.ReceiveMessageAsync(Patience, TestContext.Current.CancellationToken)
                    ?? throw new InvalidOperationException($"Message {received + 1} of {count} never reached {queueOrTopic}.");
                await receiver.DeadLetterMessageAsync(message, "TestFailure", "The test failed this message.", TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>The messages of a dead-letter queue as the test peeks them itself, to compare with what the adapter does.</summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekDeadLettersAsync(ServiceBusClient client, string queueOrTopic, string? subscription)
    {
        await using var receiver = Receiver(client, queueOrTopic, subscription, SubQueue.DeadLetter);
        return await receiver.PeekMessagesAsync(100, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Takes the next messages off a queue or subscription that has no sessions, and completes them.</summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> TakeAsync(ServiceBusClient client, string queueOrTopic, string? subscription, int count, TimeSpan? wait = null)
    {
        await using var receiver = Receiver(client, queueOrTopic, subscription);
        return await TakeAsync(receiver, count, wait);
    }

    /// <summary>Takes the next messages of one session and completes them.</summary>
    public static async Task<IReadOnlyList<ServiceBusReceivedMessage>> TakeSessionAsync(ServiceBusClient client, string topic, string subscription, string sessionId, int count)
    {
        await using var receiver = await AcceptSessionAsync(client, topic, subscription, sessionId);
        return await TakeAsync(receiver, count, wait: null);
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

    private static async Task<IReadOnlyList<ServiceBusReceivedMessage>> TakeAsync(ServiceBusReceiver receiver, int count, TimeSpan? wait)
    {
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

    private static ServiceBusReceiver Receiver(ServiceBusClient client, string queueOrTopic, string? subscription, SubQueue subQueue = SubQueue.None)
    {
        var options = new ServiceBusReceiverOptions { SubQueue = subQueue };
        return subscription is null ? client.CreateReceiver(queueOrTopic, options) : client.CreateReceiver(queueOrTopic, subscription, options);
    }

    private static Task<ServiceBusSessionReceiver> AcceptSessionAsync(ServiceBusClient client, string queueOrTopic, string? subscription, string sessionId) =>
        subscription is null
            ? client.AcceptSessionAsync(queueOrTopic, sessionId, cancellationToken: TestContext.Current.CancellationToken)
            : client.AcceptSessionAsync(queueOrTopic, subscription, sessionId, cancellationToken: TestContext.Current.CancellationToken);
}
