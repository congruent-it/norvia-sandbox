using Mediator;

namespace Norvia.Tms.Application.Shipments.ListInProgress;

/// <summary>What is on its way (P5): the Booked, Collected and In transit Shipments, with the last Tracking event and the ETA of each.</summary>
public sealed record ListShipmentsInProgressQuery : IQuery<IReadOnlyList<ShipmentInProgressSummary>>;
