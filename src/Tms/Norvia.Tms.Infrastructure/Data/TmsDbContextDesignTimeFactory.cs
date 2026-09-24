using System.Diagnostics.CodeAnalysis;
using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Norvia.Tms.Infrastructure.Outbox;

namespace Norvia.Tms.Infrastructure.Data;

[ExcludeFromCodeCoverage(Justification = "Used only by the EF Core tools to create migrations.")]
internal sealed class TmsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TmsDbContext>
{
    public TmsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TmsDbContext>().UseSqlServer("Server=design-time;Database=tms").Options, new NoDomainEvents(),
            new OutboxBuffer(TimeProvider.System));

    private sealed class NoDomainEvents : IDomainEventDispatcher
    {
        public Task DispatchAndClearEvents(IEnumerable<IHasDomainEvents> entitiesWithEvents) => Task.CompletedTask;
    }
}
