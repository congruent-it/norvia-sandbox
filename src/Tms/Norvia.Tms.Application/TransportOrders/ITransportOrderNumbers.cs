using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <summary>Hands out Transport Order numbers, each one once.</summary>
public interface ITransportOrderNumbers
{
    Task<TransportOrderNumber> NextAsync(CancellationToken cancellationToken);
}
