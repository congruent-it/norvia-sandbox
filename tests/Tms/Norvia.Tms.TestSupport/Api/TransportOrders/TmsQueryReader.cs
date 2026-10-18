extern alias TmsWeb;

using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.Tms.Application.TransportOrders.Browse;
using Norvia.Tms.Application.TransportOrders.GetDetail;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure;
using TmsWeb::Norvia.Tms.Web.Planner;
using TmsWeb::Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.TestSupport.TransportOrders;

/// <summary>
/// Runs the TMS UI's query handlers in the test process against the AppHost's SQL Server, as the TMS UI does in its own process:
/// they are not on the TMS API. Each read gets a scope of its own, like <c>TransportOrderQueries</c> in the TMS UI.
/// </summary>
internal sealed class TmsQueryReader : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TmsQueryReader(ServiceProvider services) => _services = services;

    /// <param name="tms">The TMS whose database is read.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <param name="now">The time the reads see, or null for the system's.</param>
    public static async Task<TmsQueryReader> OpenAsync(TmsFixture tms, CancellationToken cancellationToken, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(tms);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = await tms.ConnectionStringAsync("tms", cancellationToken),
            ["Seed:Path"] = Path.Combine(AppContext.BaseDirectory, "Seed", "tms.test-seed.json"),
        });
        builder.AddTmsInfrastructure();

        // Reading raises no domain event, and the mediator that would dispatch them is not part of this process.
        builder.Services.AddSingleton<IDomainEventDispatcher, NoDomainEvents>();

        // The TMS UI's adapter sends its queries through the mediator, a scope each.
        builder.Services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(ListTransportOrdersQuery), typeof(TransportOrder)];
        });
        builder.Services.AddSingleton<ITransportOrderQueries, TransportOrderQueries>();
        if (now is not null)
        {
            builder.Services.AddSingleton<TimeProvider>(new MovedTimeProvider(now));
        }

        return new(builder.Services.BuildServiceProvider());
    }

    public async Task<TransportOrderPage> ListAsync(
        string search, CancellationToken cancellationToken, TransportOrderStatus? status = null, string? shipperId = null, int page = 1, int pageSize = 25)
    {
        await using var scope = _services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<ListAllTransportOrdersHandler>(scope.ServiceProvider)
            .Handle(new ListAllTransportOrdersQuery(status, shipperId, search, page, pageSize), cancellationToken);
    }

    public async Task<Result<TransportOrderDetailView>> DetailAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<GetTransportOrderDetailHandler>(scope.ServiceProvider)
            .Handle(new GetTransportOrderDetailQuery(transportOrderNumber), cancellationToken);
    }

    /// <summary>The counts of the Planner's home page, read as the TMS UI reads them.</summary>
    public async Task<PlannerHomeCounts> PlannerHomeAsync(CancellationToken cancellationToken) =>
        (await PlannerHomeReader.ReadAsync(_services.GetRequiredService<ITransportOrderQueries>(), cancellationToken)).Single();

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private sealed class MovedTimeProvider(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }

    private sealed class NoDomainEvents : IDomainEventDispatcher
    {
        public Task DispatchAndClearEvents(IEnumerable<IHasDomainEvents> entitiesWithEvents) => Task.CompletedTask;
    }
}
