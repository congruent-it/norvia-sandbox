using System.Text.Json;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Outbox;

internal sealed class EfOutbox(TmsDbContext dbContext, TimeProvider time) : IOutbox
{
    public void Add(IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventId = integrationEvent.EventId,
            Topic = integrationEvent.Topic,
            EventType = integrationEvent.EventType,
            CorrelationId = integrationEvent.CorrelationId,
            Body = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), IntegrationEventJson.Options),
            ApplicationProperties = JsonSerializer.Serialize(integrationEvent.ApplicationProperties, IntegrationEventJson.Options),
            CreatedAt = time.GetUtcNow(),
        });
    }
}
