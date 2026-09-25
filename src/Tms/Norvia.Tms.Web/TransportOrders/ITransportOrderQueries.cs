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

    /// <summary>The Rejected bookings, which Customer service looks up when a Shipper calls about one.</summary>
    Task<IReadOnlyList<TransportOrderSummary>> ListRejectedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The Rejected booking with this ID, or null when there is none. A Rejected booking has no Transport Order number,
    /// so it is found by its ID.
    /// </summary>
    Task<TransportOrderView?> FindRejectedAsync(Guid id, CancellationToken cancellationToken);
}
