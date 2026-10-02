namespace Norvia.AppHost;

/// <summary>
/// Locally there is no Entra, so the TMS trusts tokens signed with a key the AppHost holds as a secret parameter.
/// The Aspire test host sets that key and issues its own tokens, with or without the intake role. The local intake
/// Logic App's client ID stands in for its managed identity, which in Azure is the only caller on the Shipper API channel.
/// The local booking worker's client ID stands in for the P4 booking Logic App's, the one holder of the report role.
/// The local answer Logic App's client ID, which the stand-in Partner API signs its token with, is the one holder of the answer role.
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
    public const string AnswerLogicAppClientId = "norvia-local-answer-logic-app";
    public const string TmsUiClientId = "norvia-local-tms-ui";
    public const string PartnerApiAudience = "api://norvia-partner-api";

    /// <summary>The app role a Shipper Persona holds on the Norvia Partner API, as the estate grants it in Entra.</summary>
    public const string SubmitRole = "TransportOrders.Submit";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API, as the estate grants it in Entra.</summary>
    public const string RespondRole = "CarrierBookings.Respond";

    /// <summary>A Persona's local client ID, which stands in for its Entra client app.</summary>
    public static string PersonaClientId(string personaId) => $"norvia-local-persona-{personaId}";
}
