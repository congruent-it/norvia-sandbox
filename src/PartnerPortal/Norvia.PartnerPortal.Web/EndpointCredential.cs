using System.Security.Cryptography;
using System.Text;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web;

/// <summary>The check every Persona endpoint makes of its caller: the credential Key Vault holds for the party.</summary>
internal static class EndpointCredential
{
    public const string ApiKeyHeader = "X-Api-Key";

    public static bool IsOf(string partyId, string? apiKey, IReadOnlyDictionary<string, string> apiKeys) =>
        !string.IsNullOrEmpty(apiKey)
        && apiKeys.TryGetValue(partyId, out var expected)
        && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(expected));

    /// <summary>One failure for each Persona of this kind that has no credential, for the options' startup validation.</summary>
    public static IEnumerable<string> Missing(IPersonaDirectory personas, PartyType partyType, string section, IReadOnlyDictionary<string, string> apiKeys) =>
        personas.All
            .Where(persona => persona.PartyType == partyType && !(apiKeys.TryGetValue(persona.PartyId, out var key) && !string.IsNullOrWhiteSpace(key)))
            .Select(persona => $"{section}:ApiKeys:{persona.PartyId} needs the credential of {persona.DisplayName}'s endpoint.");
}
