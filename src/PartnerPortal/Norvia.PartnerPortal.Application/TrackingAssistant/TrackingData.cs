using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>What the portal fetched for one question with the Persona's own token: the open order, the first page of the list, and the order a named reference or number led to.</summary>
public sealed record TrackingData(ShipperTrackingDetail? Detail, IReadOnlyList<ShipperTrackingSummary> Orders, ShipperTrackingDetail? Found);
