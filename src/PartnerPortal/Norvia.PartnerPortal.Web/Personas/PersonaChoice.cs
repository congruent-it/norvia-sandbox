using Norvia.PartnerPortal.Application.Personas;

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

    public static string BookingsPath(string personaId) => $"/personas/{Uri.EscapeDataString(personaId)}/bookings";

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
