using Mediator;

namespace Norvia.Erp.Application.BillableLines.ListWaiting;

/// <summary>The Shippers with Waiting Billable lines, with the number of lines and their total, by Shipper name.</summary>
public sealed record ListWaitingBillableLinesQuery : IQuery<IReadOnlyList<WaitingShipperView>>;

public sealed record WaitingShipperView(string ShipperId, string ShipperName, int LineCount, decimal Total);
