using Ardalis.Specification;

namespace Norvia.Erp.Infrastructure.Outbox;

/// <summary>The oldest outbox messages not yet sent to Service Bus.</summary>
internal sealed class UnpublishedOutboxMessagesSpec : Specification<OutboxMessage>
{
    public UnpublishedOutboxMessagesSpec(int batchSize) =>
        Query.Where(message => message.PublishedAt == null)
            .OrderBy(message => message.Id)
            .Take(batchSize);
}
