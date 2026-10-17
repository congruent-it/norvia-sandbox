namespace Norvia.Tms.Application.TransportOrders.Browse;

/// <param name="Items">The Transport Orders on this page.</param>
/// <param name="TotalCount">How many Transport Orders match the filters and the search, on every page.</param>
public sealed record TransportOrderPage(IReadOnlyList<TransportOrderListItem> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => (TotalCount + PageSize - 1) / PageSize;
}
