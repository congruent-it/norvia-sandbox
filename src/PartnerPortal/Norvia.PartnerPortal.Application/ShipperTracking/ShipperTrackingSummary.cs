namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>
/// One Transport Order in the Shipper's list. Times are the local times the API returns, <c>dd-MM-yyyy HH:mm</c>, kept
/// as returned: the ETA and the last update are on the delivery address's clock.
/// </summary>
public sealed record ShipperTrackingSummary(
    string TransportOrderNumber,
    string? ShipperReference,
    ShipperTrackingStatus Status,
    ShipperPlace Pickup,
    ShipperPlace Receiver,
    string? Eta,
    string? LastUpdate);
