using System.Text.Json;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Infrastructure.Outbox;

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
        };
    }
}
