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

    /// <summary>
    /// Where the Partner portal takes the Shipper invoices and Credit notes Norvia's E1 Shipper invoice Logic App sends a Shipper Persona
    /// (ShipperInvoiceEndpoint has the same shape); a Carrier has none.
    /// </summary>
    public string? InvoicePath => IsShipper ? $"/shipper/v1/{PartyId}/invoices" : null;

    /// <summary>
    /// Where the Partner portal takes what Norvia's P7 Carrier cancellation Logic App sends a Carrier Persona (CarrierCancellationEndpoint
    /// has the same shape); a Shipper has none.
    /// </summary>
    public string? CancellationPath => IsShipper ? null : $"/carrier/v1/{PartyId}/cancellations";

    /// <summary>
    /// Where the Partner portal takes the outcome Norvia's E2 Carrier invoice outcome Logic App sends a Carrier Persona
    /// (CarrierInvoiceOutcomeEndpoint has the same shape); a Shipper has none.
    /// </summary>
    public string? InvoiceOutcomePath => IsShipper ? null : $"/carrier/v1/{PartyId}/invoice-outcomes";

    public static IReadOnlyList<SeedPersona> ReadAll(string seedFile)
    {
        using var seed = JsonDocument.Parse(File.ReadAllText(seedFile));
        return [.. seed.RootElement.GetProperty("personas").EnumerateArray().Select(persona => new SeedPersona(
            persona.GetProperty("id").GetString()!,
            persona.GetProperty("party").GetProperty("type").GetString()!,
            persona.GetProperty("party").GetProperty("id").GetString()!))];
    }
}
