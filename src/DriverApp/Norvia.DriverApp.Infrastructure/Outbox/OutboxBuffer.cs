using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Infrastructure.Outbox;

/// <summary>
/// Holds the events raised while handling one request until <see cref="Data.DriverAppDbContext"/> saves them as outbox messages in the same
/// transaction as the report that raised them.
/// </summary>
internal sealed class OutboxBuffer(TimeProvider time) : IDriverEventOutbox
{
    private readonly List<OutboxMessage> _pending = [];

    public void Add(DriverAppEvent driverAppEvent) => _pending.Add(OutboxMessage.From(driverAppEvent, time.GetUtcNow()));

    public IReadOnlyList<OutboxMessage> TakePending()
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        return pending;
    }
}
