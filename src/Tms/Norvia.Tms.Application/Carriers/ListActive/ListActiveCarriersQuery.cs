using Mediator;
using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Application.Carriers.ListActive;

/// <summary>The Carriers a Planner may assign to: the active ones, by name. An inactive Carrier is never offered.</summary>
public sealed record ListActiveCarriersQuery : IQuery<IReadOnlyList<CarrierView>>;
