namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// What an operation of the Norvia Partner API asks of its caller beyond the subscription key and a valid token: the app
/// role, and the party (Shipper or Carrier) the token's <c>azp</c> maps to, each with the policy's refusal.
/// </summary>
internal sealed record EdgeRule(
    string Role,
    string NotAllowedText,
    string NotAllowedCode,
    Func<PartnerApiOptions, IReadOnlyDictionary<string, string>> PartiesByClientId,
    string NoPartyText,
    string NoPartyCode)
{
    /// <summary>infra/estate/partner-api/submit-transport-order.policy.xml</summary>
    public static EdgeRule SubmitTransportOrder { get; } = new(
        "TransportOrders.Submit",
        "This client may not submit Transport Orders.",
        "NotAllowedToSubmit",
        options => options.ShippersByClientId,
        "No Shipper is registered for this client.",
        "NoShipperForClient");

    /// <summary>infra/estate/partner-api/answer-carrier-booking.policy.xml</summary>
    public static EdgeRule AnswerCarrierBooking { get; } = new(
        "CarrierBookings.Respond",
        "This client may not answer Carrier bookings.",
        "NotAllowedToRespond",
        options => options.CarriersByClientId,
        "No Carrier is registered for this client.",
        "NoCarrierForClient");

    /// <summary>infra/estate/partner-api/submit-carrier-tracking.policy.xml</summary>
    public static EdgeRule SubmitCarrierTracking { get; } = new(
        "Tracking.Submit",
        "This client may not submit tracking.",
        "NotAllowedToSubmitTracking",
        options => options.CarriersByClientId,
        "No Carrier is registered for this client.",
        "NoCarrierForClient");

    /// <summary>infra/estate/partner-api/submit-carrier-proof-of-delivery.policy.xml</summary>
    public static EdgeRule SubmitCarrierProofOfDelivery { get; } = new(
        "ProofsOfDelivery.Submit",
        "This client may not submit Proofs of delivery.",
        "NotAllowedToSubmitProofOfDelivery",
        options => options.CarriersByClientId,
        "No Carrier is registered for this client.",
        "NoCarrierForClient");

    /// <summary>
    /// infra/estate/partner-api/ list-, get-, find-shipper-transport-order and get-shipper-proof-of-delivery-file .policy.xml: the
    /// four operations of the Shipper tracking API (P8) ask the same of their caller.
    /// </summary>
    public static EdgeRule ReadShipperTracking { get; } = new(
        "Tracking.Read",
        "This client may not read tracking.",
        "NotAllowedToReadTracking",
        options => options.ShippersByClientId,
        "No Shipper is registered for this client.",
        "NoShipperForClient");
}
