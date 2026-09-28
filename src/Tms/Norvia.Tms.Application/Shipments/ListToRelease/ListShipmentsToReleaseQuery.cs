using Mediator;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>The Planner's Shipments to release: the Planned Shipments, with who carries each, in the order they were planned.</summary>
public sealed record ListShipmentsToReleaseQuery : IQuery<IReadOnlyList<ShipmentToReleaseSummary>>;
