using Mediator;

namespace Norvia.Erp.Application.BillableLines.ListWaitingOfShipper;

/// <summary>The Waiting Billable lines of one Shipper, each with everything it holds; null for a Shipper the ERP doesn't know.</summary>
public sealed record ListWaitingBillableLinesOfShipperQuery(string ShipperId) : IQuery<WaitingShipperLinesView?>;

public sealed record WaitingShipperLinesView(string ShipperId, string ShipperName, decimal Total, IReadOnlyList<BillableLineView> Lines);
