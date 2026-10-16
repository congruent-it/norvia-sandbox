namespace Norvia.Simulator.Domain.Trips;

/// <summary>The Trips the Planner can build for a day, and the Shipments that found no room: no Vehicle left that carries them, or no Driver with a Shift and the licence for it.</summary>
public sealed record TripBuild(IReadOnlyList<TripDraft> Drafts, IReadOnlyList<TripShipment> Unplaced);
