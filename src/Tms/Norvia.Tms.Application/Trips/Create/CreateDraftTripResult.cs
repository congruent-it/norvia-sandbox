namespace Norvia.Tms.Application.Trips.Create;

/// <param name="Warnings">What the Planner should know about the Trip, such as a Shipment whose delivery window doesn't include its day.</param>
public sealed record CreateDraftTripResult(string Number, IReadOnlyList<TripWarning>? Warnings = null);
