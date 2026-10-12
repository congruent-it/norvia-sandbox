namespace Norvia.AppHost;

/// <summary>
/// Locally there is no Entra, so the TMS trusts tokens signed with a key the AppHost holds as a secret parameter.
/// The Aspire test host sets that key and issues its own tokens, with or without the intake role. The local intake
/// Logic App's client ID stands in for its managed identity, which in Azure is the only caller on the Shipper API channel.
/// The local booking worker's client ID stands in for the P4 booking Logic App's, the one holder of the report role.
/// The local Shipper notification worker's client ID stands in for the P7 Shipper notification Logic App's, the one holder of the Shipper notification report role.
/// The local Carrier cancellation worker's client ID stands in for the P7 Carrier cancellation Logic App's, which holds the Carrier booking report role beside the booking worker.
/// The local Trip dispatch worker's client ID stands in for the P11 Trip dispatch Logic App's, the one holder of the Trip delivery report role.
/// The local Driver reports worker's client ID stands in for the P12 Driver reports Logic App's, the one holder of the Trip start role and the Driver app's outcome role.
/// The local tracking worker's client ID stands in for the Carrier-tracking Logic App's, the one holder of the record role.
/// The local Proof of delivery worker's client ID stands in for the Carrier Proof of delivery Logic App's, the one holder of the Proof of delivery record role.
/// The local answer Logic App's client ID, which the stand-in Partner API signs its token with, is the one holder of the answer role.
/// The local API Management client ID, which the stand-in Partner API signs its Shipper tracking token with, is the one holder of the Shipper tracking role.
/// The TMS UI signs its own token for the TMS API with the key too, as its managed identity holding the manage role.
/// The local Partner API trusts the same key for the Personas' tokens, which the Partner portal signs itself (ADR 0023).
/// </summary>
public static class LocalTokens
{
    public const string SigningKeyParameter = "local-token-signing-key";
    public const string Issuer = "https://norvia.local/tokens";
    public const string TmsAudience = "api://norvia-tms";
    public const string IntakeLogicAppClientId = "norvia-local-intake-logic-app";
    public const string BookingLogicAppClientId = "norvia-local-booking-logic-app";
    public const string ShipperNotificationLogicAppClientId = "norvia-local-shipper-notification-logic-app";
    public const string CarrierCancellationLogicAppClientId = "norvia-local-carrier-cancellation-logic-app";
    public const string TripDispatchLogicAppClientId = "norvia-local-trip-dispatch-logic-app";
    public const string DriverReportsLogicAppClientId = "norvia-local-driver-reports-logic-app";
    public const string DriverAppAudience = "api://norvia-driver-app";
    public const string AnswerLogicAppClientId = "norvia-local-answer-logic-app";
    public const string TrackingLogicAppClientId = "norvia-local-tracking-logic-app";
    public const string ProofOfDeliveryLogicAppClientId = "norvia-local-proof-of-delivery-logic-app";
    public const string ApiManagementClientId = "norvia-local-api-management";
    public const string TmsUiClientId = "norvia-local-tms-ui";
    public const string PartnerApiAudience = "api://norvia-partner-api";

    /// <summary>The app role a Shipper Persona holds on the Norvia Partner API, as the estate grants it in Entra.</summary>
    public const string SubmitRole = "TransportOrders.Submit";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API, as the estate grants it in Entra.</summary>
    public const string RespondRole = "CarrierBookings.Respond";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API to report tracking, as the estate grants it in Entra.</summary>
    public const string TrackingSubmitRole = "Tracking.Submit";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API to submit Proofs of delivery, as the estate grants it in Entra.</summary>
    public const string ProofsOfDeliverySubmitRole = "ProofsOfDelivery.Submit";

    /// <summary>The app role a Shipper Persona holds on the Norvia Partner API to read its tracking, as the estate grants it in Entra. A Carrier Persona doesn't.</summary>
    public const string TrackingReadRole = "Tracking.Read";

    /// <summary>A Persona's local client ID, which stands in for its Entra client app.</summary>
    public static string PersonaClientId(string personaId) => $"norvia-local-persona-{personaId}";
}
