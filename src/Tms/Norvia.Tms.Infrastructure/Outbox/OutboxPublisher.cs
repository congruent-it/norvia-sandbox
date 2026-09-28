using System.Diagnostics;
using System.Text.Json;
using Ardalis.SharedKernel;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Infrastructure.Outbox;

/// <summary>
/// Sends committed outbox messages to Service Bus, oldest first. Delivery is at least once: a message whose send
/// succeeded but whose mark failed goes again, with the same message ID. Each is sent in a span of its own that continues
/// the trace of the work that raised its event, so the Integration console shows the publish as caused by it, and whose
/// ID the message carries as its <c>Diagnostic-Id</c>, Service Bus's trace context, for its subscribers.
/// </summary>
internal sealed partial class OutboxPublisher(
    IServiceScopeFactory scopes,
    ServiceBusClient serviceBus,
    TimeProvider time,
    IHostEnvironment environment,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const string DiagnosticIdProperty = "Diagnostic-Id";

    private readonly Dictionary<string, ServiceBusSender> _senders = [];
    private readonly ActivitySource _activities = new(environment.ApplicationName);

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

        _activities.Dispose();
    }

    // The trace context of the work that raised the event, or none, which starts a trace of the publish's own.
    private static ActivityContext RaisedIn(OutboxMessage message) =>
        ActivityContext.TryParse(message.TraceParent, traceState: null, out var raisedIn) ? raisedIn : default;

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var outboxMessages = scope.ServiceProvider.GetRequiredService<IRepository<OutboxMessage>>();

        var pending = await outboxMessages.ListAsync(new UnpublishedOutboxMessagesSpec(options.Value.BatchSize), cancellationToken);

        foreach (var message in pending)
        {
            using var span = _activities.StartActivity($"publish {message.EventType}", ActivityKind.Producer, RaisedIn(message));
            // The message's application properties are its tracking-contract keys, so its traces join its Message trail.
            using var trail = logger.BeginScope(ApplicationPropertiesOf(message));
            var serviceBusMessage = ToServiceBusMessage(message);
            if ((span?.Id ?? message.TraceParent) is { } traceContext)
            {
                serviceBusMessage.ApplicationProperties[DiagnosticIdProperty] = traceContext;
            }

            await SenderFor(message.Topic).SendMessageAsync(serviceBusMessage, cancellationToken);
            message.MarkPublished(time.GetUtcNow());
            await outboxMessages.UpdateAsync(message, cancellationToken);

            // Publishing is a hop of its own on the trail, so it records its outcome; the message doesn't carry it. A
            // failed send is retried on the next cycle, and records nothing yet.
            using (logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.Outcome] = TrackingKeys.Succeeded }))
            {
                LogPublished(message.EventType, message.EventId, message.CorrelationId);
            }
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

        foreach (var (name, value) in ApplicationPropertiesOf(message))
        {
            serviceBusMessage.ApplicationProperties[name] = value;
        }

        return serviceBusMessage;
    }

    private static Dictionary<string, object?> ApplicationPropertiesOf(OutboxMessage message)
    {
        using var properties = JsonDocument.Parse(message.ApplicationProperties);
        return properties.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => (object?)(property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True or JsonValueKind.False => property.Value.GetBoolean(),
                _ => property.Value.GetString(),
            }));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} {EventId} for correlation ID {CorrelationId}")]
    private partial void LogPublished(string eventType, Guid eventId, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing outbox messages failed; retrying on the next cycle")]
    private partial void LogPublishFailed(Exception exception);
}
