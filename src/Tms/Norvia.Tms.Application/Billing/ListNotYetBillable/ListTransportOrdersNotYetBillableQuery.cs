using Mediator;

namespace Norvia.Tms.Application.Billing.ListNotYetBillable;

/// <summary>Customer service's list of Delivered Transport-only orders that aren't Billable yet (P9), oldest delivery first.</summary>
public sealed record ListTransportOrdersNotYetBillableQuery : IQuery<IReadOnlyList<TransportOrderNotYetBillableSummary>>;
