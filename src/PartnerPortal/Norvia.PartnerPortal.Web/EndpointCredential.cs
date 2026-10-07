using System.Security.Cryptography;
using System.Text;

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
}
