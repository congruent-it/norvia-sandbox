using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.Tms.Application.TransportOrders.Browse;
using Norvia.Tms.Application.TransportOrders.GetDetail;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Runs the TMS UI's query handlers in the test process against the AppHost's SQL Server, as the TMS UI does in its own process:
/// they are not on the TMS API. Each read gets a scope of its own, like <c>TransportOrderQueries</c> in the TMS UI.
/// </summary>
internal sealed class TmsQueryReader : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TmsQueryReader(ServiceProvider services) => _services = services;

    public static async Task<TmsQueryReader> OpenAsync(TmsFixture tms, CancellationToken cancellationToken)
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

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private sealed class NoDomainEvents : IDomainEventDispatcher
    {
        public Task DispatchAndClearEvents(IEnumerable<IHasDomainEvents> entitiesWithEvents) => Task.CompletedTask;
    }
}
