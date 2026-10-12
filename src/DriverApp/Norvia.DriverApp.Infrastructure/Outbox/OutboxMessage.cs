using System.Diagnostics;
using System.Text.Json;
using Ardalis.SharedKernel;
using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Infrastructure.Outbox;

/// <summary>A Driver app event waiting to be published, stored in the same transaction as the report that caused it.</summary>
internal sealed class OutboxMessage : IAggregateRoot
{
    public long Id { get; private set; }

    public required Guid EventId { get; init; }

    public required string Topic { get; init; }

    public required string EventType { get; init; }

    public required string CorrelationId { get; init; }

    /// <summary>The Service Bus session the message belongs to: the Trip number, so one Trip's reports are read in order.</summary>
    public required string SessionId { get; init; }

    public required string Body { get; init; }

    /// <summary>The message's application properties, as a JSON object.</summary>
    public required string ApplicationProperties { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The W3C trace context of the work that raised the event, which publishing it continues; null when there was none.</summary>
    public string? TraceParent { get; init; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public void MarkPublished(DateTimeOffset publishedAt) => PublishedAt = publishedAt;

    public static OutboxMessage From(DriverAppEvent driverAppEvent, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(driverAppEvent);
        return new OutboxMessage
        {
            EventId = driverAppEvent.EventId,
            Topic = driverAppEvent.Topic,
            EventType = driverAppEvent.EventType,
            CorrelationId = driverAppEvent.CorrelationId,
            SessionId = driverAppEvent.SessionId,
            Body = JsonSerializer.Serialize(driverAppEvent, driverAppEvent.GetType(), DriverAppEventJson.Options),
            ApplicationProperties = JsonSerializer.Serialize(driverAppEvent.ApplicationProperties, DriverAppEventJson.Options),
            CreatedAt = createdAt,
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } raisedIn ? raisedIn.Id : null,
        };
    }
}
