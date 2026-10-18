using Ardalis.SharedKernel;

namespace Norvia.Erp.Infrastructure.Data;

/// <summary>For a <see cref="ErpDbContext"/> that changes the schema only, so no aggregate raises an event.</summary>
internal sealed class NoDomainEvents : IDomainEventDispatcher
{
    public Task DispatchAndClearEvents(IEnumerable<IHasDomainEvents> entitiesWithEvents) => Task.CompletedTask;
}
