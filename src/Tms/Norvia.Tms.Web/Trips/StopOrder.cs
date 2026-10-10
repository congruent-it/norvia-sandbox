using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Web.Trips;

/// <summary>A Stop as the Planner puts it in the sequence: a Shipment's collection or delivery.</summary>
public sealed record StopOrder(string ShipmentNumber, StopKind Kind);
