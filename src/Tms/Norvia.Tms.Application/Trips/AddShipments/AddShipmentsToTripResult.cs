namespace Norvia.Tms.Application.Trips.AddShipments;

/// <param name="Warnings">What the Planner should know about the Trip now, such as a Shipment whose delivery window doesn't include its day.</param>
public sealed record AddShipmentsToTripResult(string Number, IReadOnlyList<TripWarning> Warnings);
