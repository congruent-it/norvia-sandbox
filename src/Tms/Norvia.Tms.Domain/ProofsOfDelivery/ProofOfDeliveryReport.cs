using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>
/// What a sender said about a delivery (P6): who sent it, under which document ID, about which Carrier booking, who signed,
/// when and where, in which condition, and its files. It travels as one value from the call to the Proof of delivery that
/// keeps it; the call's own correlation ID and when the TMS received it are not the sender's words.
/// </summary>
/// <param name="Source">Who sent it.</param>
/// <param name="DocumentId">The sender's own ID for the Proof of delivery, unique per source.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking the Proof of delivery is about.</param>
/// <param name="ReceivedBy">The name of the person who signed.</param>
/// <param name="DeliveryTime">When the goods were delivered, with the offset of where.</param>
/// <param name="Files">The files, already described: the content is kept apart.</param>
/// <param name="CarrierBookingNumber">The TMS's own Carrier booking number, used only when the reference doesn't match.</param>
public sealed record ProofOfDeliveryReport(
    ProofOfDeliverySource Source,
    string DocumentId,
    string CarrierBookingReference,
    string ReceivedBy,
    DateTimeOffset DeliveryTime,
    TrackingLocation Location,
    DeliveryCondition Condition,
    string? Remark,
    IReadOnlyList<ProofOfDeliveryFile> Files,
    string? CarrierBookingNumber = null);
