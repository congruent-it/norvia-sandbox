using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.Personas;

/// <summary>
/// Which Persona the presenter plays. Every page that acts as a Persona has it in its address, so everything done there
/// is done as that party; a cookie remembers the Persona last switched to, which the portal's home opens.
/// </summary>
public static class PersonaChoice
{
    /// <summary>The cookie that remembers the Persona a browser last switched to.</summary>
    public const string CookieName = "norvia-partner-persona";

    /// <summary>The route parameter every Persona's page takes.</summary>
    public const string RouteParameter = "PersonaId";

    public static string NewBookingPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/bookings/new";

    public static string FillFromEmailPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/bookings/from-email";

    public static string BookingsPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/bookings";

    public static string BookingRequestsPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/booking-requests";

    public static string ShipmentsPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/shipments";

    public static string DraftInvoicePath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/draft-invoice";

    public static string CarrierInvoicesPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/carrier-invoices";

    public static string NotificationsPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/notifications";

    public static string InvoicesPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/invoices";

    public static string TrackingPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/tracking";

    public static string TrackingPath(string personaId, string transportOrderNumber) =>
        $"{TrackingPath(personaId)}/{Uri.EscapeDataString(transportOrderNumber)}";

    /// <summary>Where a Transport Order's Proof of delivery file is downloaded through the portal.</summary>
    public static string ProofOfDeliveryFilePath(string personaId, string transportOrderNumber, Guid fileId) =>
        $"{TrackingPath(personaId, transportOrderNumber)}/proof-of-delivery/files/{fileId}";

    /// <summary>Where a Persona's work starts: a Shipper books a transport, a Carrier opens its inbox.</summary>
    public static string HomePath(PersonaView persona)
    {
        ArgumentNullException.ThrowIfNull(persona);
        return persona.PartyType == PartyType.Carrier ? BookingRequestsPath(persona.Id) : NewBookingPath(persona.Id);
    }

    /// <summary>The Persona whose page this is; elsewhere the one last switched to, or the first.</summary>
    public static PersonaView Current(HttpRequest request, IReadOnlyList<PersonaView> personas)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(personas);
        return Find(request.RouteValues[RouteParameter] as string, personas)
            ?? Find(request.Cookies[CookieName], personas)
            ?? personas[0];
    }

    public static PersonaView? Find(string? personaId, IReadOnlyList<PersonaView> personas) =>
        personas?.FirstOrDefault(persona => persona.Id == personaId);
}
