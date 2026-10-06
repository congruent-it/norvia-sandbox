using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

/// <summary>
/// Sends a Proof of delivery for one of a Carrier Persona's Shipments to Norvia, through the Partner API, with a unique
/// document ID and a location taken from the Shipment's delivery address. Not found when the Persona has no such Carrier
/// Persona or Shipment; Invalid when received by is blank, a remark is missing with reservations, the files aren't 1 to 5
/// of at most 10 MB, or the delivery address can't give a location.
/// </summary>
public sealed record SendProofOfDeliveryCommand(
    string PersonaId,
    string CarrierBookingNumber,
    string ReceivedBy,
    ProofOfDeliveryCondition Condition,
    string? Remark,
    IReadOnlyList<ProofOfDeliveryUpload> Files)
    : ICommand<Result<SentProofOfDelivery>>;
