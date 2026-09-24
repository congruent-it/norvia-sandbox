using System.Text.Json;
using Ardalis.SharedKernel;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norvia.Tms.Infrastructure.Outbox;

/// <summary>
/// Sends committed outbox messages to Service Bus, oldest first. Delivery is at least once: a message whose send
/// succeeded but whose mark failed goes again, with the same message ID.
/// </summary>
internal sealed partial class OutboxPublisher(
    IServiceScopeFactory scopes,
    ServiceBusClient serviceBus,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly Dictionary<string, ServiceBusSender> _senders = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, time);
        do
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPublishFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var outboxMessages = scope.ServiceProvider.GetRequiredService<IRepository<OutboxMessage>>();

        var pending = await outboxMessages.ListAsync(new UnpublishedOutboxMessagesSpec(options.Value.BatchSize), cancellationToken);

        foreach (var message in pending)
        {
            await SenderFor(message.Topic).SendMessageAsync(ToServiceBusMessage(message), cancellationToken);
            message.MarkPublished(time.GetUtcNow());
            await outboxMessages.UpdateAsync(message, cancellationToken);
            LogPublished(message.EventType, message.EventId, message.CorrelationId);
        }
    }

    private ServiceBusSender SenderFor(string topic)
    {
        if (!_senders.TryGetValue(topic, out var sender))
        {
            sender = serviceBus.CreateSender(topic);
            _senders[topic] = sender;
        }

        return sender;
    }

    internal static ServiceBusMessage ToServiceBusMessage(OutboxMessage message)
    {
        var serviceBusMessage = new ServiceBusMessage(BinaryData.FromString(message.Body))
        {
            MessageId = message.EventId.ToString(),
            Subject = message.EventType,
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
        };

        using var properties = JsonDocument.Parse(message.ApplicationProperties);
        foreach (var property in properties.RootElement.EnumerateObject())
        {
            serviceBusMessage.ApplicationProperties[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True or JsonValueKind.False => property.Value.GetBoolean(),
                _ => property.Value.GetString(),
            };
        }

        return serviceBusMessage;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} {EventId} for correlation ID {CorrelationId}")]
    private partial void LogPublished(string eventType, Guid eventId, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing outbox messages failed; retrying on the next cycle")]
    private partial void LogPublishFailed(Exception exception);
}
