using System.Diagnostics;
using System.Text.Json;
using Ardalis.SharedKernel;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Infrastructure.Outbox;

/// <summary>An integration event waiting to be published, stored in the same transaction as the change that caused it.</summary>
internal sealed class OutboxMessage : IAggregateRoot
{
    public long Id { get; private set; }

    public required Guid EventId { get; init; }

    public required string Topic { get; init; }

    public required string EventType { get; init; }

    public required string CorrelationId { get; init; }

    public required string Body { get; init; }

    /// <summary>The message's application properties, as a JSON object.</summary>
    public required string ApplicationProperties { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The W3C trace context of the work that raised the event, such as the intake's request or the rating worker's
    /// span, which publishing it continues; null when there was none.
    /// </summary>
    public string? TraceParent { get; init; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public void MarkPublished(DateTimeOffset publishedAt) => PublishedAt = publishedAt;

    public static OutboxMessage From(IntegrationEvent integrationEvent, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return new OutboxMessage
        {
            EventId = integrationEvent.EventId,
            Topic = integrationEvent.Topic,
            EventType = integrationEvent.EventType,
            CorrelationId = integrationEvent.CorrelationId,
            Body = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), IntegrationEventJson.Options),
            ApplicationProperties = JsonSerializer.Serialize(integrationEvent.ApplicationProperties, IntegrationEventJson.Options),
            CreatedAt = createdAt,
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } raisedIn ? raisedIn.Id : null,
        };
    }
}
