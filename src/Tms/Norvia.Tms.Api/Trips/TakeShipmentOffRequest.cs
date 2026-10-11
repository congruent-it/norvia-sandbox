namespace Norvia.Tms.Api.Trips;

/// <param name="Reason">Why the Planner takes the Shipment off its Trip, in their words. Required.</param>
public sealed record TakeShipmentOffRequest(string? Reason);
