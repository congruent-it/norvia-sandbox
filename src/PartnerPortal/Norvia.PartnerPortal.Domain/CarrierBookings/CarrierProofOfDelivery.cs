namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// One Proof of delivery a Carrier Persona sends for a Shipment: who received the goods, when and where, in what condition,
/// and the files that back it. The place is the Shipment's delivery address. It never names the Carrier: Norvia takes it
/// from the token (ADR 0005).
/// </summary>
/// <param name="DocumentId">The Carrier's own ID for the Proof of delivery, unique per Carrier.</param>
/// <param name="BookingReference">The Carrier's own reference for its booking.</param>
/// <param name="Remark">Required with reservations.</param>
/// <param name="Country">The ISO 3166-1 alpha-3 country code.</param>
public sealed record CarrierProofOfDelivery(
    string DocumentId,
    string BookingReference,
    string CarrierBookingNumber,
    string ReceivedBy,
    DateTimeOffset DeliveryTime,
    string Place,
    string Postcode,
    string Country,
    ProofOfDeliveryCondition Condition,
    string? Remark,
    IReadOnlyList<CarrierProofOfDeliveryFile> Files);
