using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Infrastructure.Outbox;

/// <summary>
/// Holds the integration events raised while handling one request until <see cref="Data.ErpDbContext"/> saves them
/// as outbox messages in the same transaction as the change that raised them.
/// </summary>
internal sealed class OutboxBuffer(TimeProvider time) : IOutbox
{
    private readonly List<OutboxMessage> _pending = [];

    public void Add(IntegrationEvent integrationEvent) => _pending.Add(OutboxMessage.From(integrationEvent, time.GetUtcNow()));

    public IReadOnlyList<OutboxMessage> TakePending()
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        return pending;
    }
}
