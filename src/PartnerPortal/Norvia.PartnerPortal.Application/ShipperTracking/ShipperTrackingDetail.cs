namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>
/// One Transport Order with everything the Shipper sees of it. Times are the local times the API returns,
/// <c>dd-MM-yyyy HH:mm</c>, kept as returned: the pickup window is on the pickup address's clock, the others on the
/// delivery address's.
/// </summary>
public sealed record ShipperTrackingDetail(
    string TransportOrderNumber,
    string? ShipperReference,
    ShipperTrackingStatus Status,
    ShipperPlace Pickup,
    string? PickupFrom,
    string? PickupUntil,
    ShipperPlace Receiver,
    string? DeliverFrom,
    string? DeliverUntil,
    string? Eta,
    string? LastUpdate,
    IReadOnlyList<ShipperProgressEvent> Progress,
    ShipperProofOfDelivery? ProofOfDelivery);
