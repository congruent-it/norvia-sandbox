using System.Text.Json;

namespace Norvia.AppHost;

/// <summary>
/// A Persona the Partner portal plays, as the seed file names it. The estate Bicep gives each one its Entra client app
/// and subscription in Azure (identities.bicep, partner-api.bicep); the AppHost gives each one a local client ID and a
/// subscription key for the run (ADR 0023).
/// </summary>
public sealed record SeedPersona(string Id, string PartyType, string PartyId)
{
    public bool IsShipper => PartyType == "Shipper";

    /// <summary>
    /// Where the Partner portal takes what Norvia's Logic Apps send this Persona: a Shipper's notifications, a Carrier's
    /// booking requests. The portal's own endpoints (ShipperNotificationEndpoint, CarrierBookingEndpoint) have the same
    /// shapes, which the AppHost model tests pin from this side.
    /// </summary>
    public string EndpointPath => IsShipper ? $"/shipper/v1/{PartyId}/notifications" : $"/carrier/v1/{PartyId}/bookings";

    public static IReadOnlyList<SeedPersona> ReadAll(string seedFile)
    {
        using var seed = JsonDocument.Parse(File.ReadAllText(seedFile));
        return [.. seed.RootElement.GetProperty("personas").EnumerateArray().Select(persona => new SeedPersona(
            persona.GetProperty("id").GetString()!,
            persona.GetProperty("party").GetProperty("type").GetString()!,
            persona.GetProperty("party").GetProperty("id").GetString()!))];
    }
}
