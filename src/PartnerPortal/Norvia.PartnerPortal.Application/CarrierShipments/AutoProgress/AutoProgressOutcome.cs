namespace Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;

/// <param name="NextDueAt">When the next automatic Tracking event is due; null when no Shipment has one.</param>
public sealed record AutoProgressOutcome(DateTimeOffset? NextDueAt);
