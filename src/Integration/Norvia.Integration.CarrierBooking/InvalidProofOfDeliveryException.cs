namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// A Carrier's Proof of delivery can't be mapped: it isn't a JSON object, a field the Canonical Model requires is missing
/// or unreadable, or a file isn't valid base64. The Carrier Proof of delivery Logic App's run fails, so the message goes
/// to the Proof of delivery queue's dead-letter queue (P6).
/// </summary>
public sealed class InvalidProofOfDeliveryException(string message) : Exception(message);
