using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.List;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>What the TMS UI reads about Transport Orders, all of it through the TMS query side.</summary>
public interface ITransportOrderQueries
{
    /// <summary>The Accepted Transport Orders, which wait for planning.</summary>
    Task<IReadOnlyList<TransportOrderSummary>> ListWaitingForPlanningAsync(CancellationToken cancellationToken);

    /// <summary>The Transport Order with this number, or null when there is none.</summary>
    Task<TransportOrderView?> FindAsync(string transportOrderNumber, CancellationToken cancellationToken);
}
