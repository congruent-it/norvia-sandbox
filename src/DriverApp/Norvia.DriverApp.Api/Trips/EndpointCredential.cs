using System.Security.Cryptography;
using System.Text;

namespace Norvia.DriverApp.Api.Trips;

/// <summary>The check the Trip endpoint makes of its caller: the credential Key Vault holds for the Driver app's endpoint.</summary>
internal static class EndpointCredential
{
    public const string ApiKeyHeader = "X-Api-Key";

    public static bool IsValid(string? apiKey, string expected) =>
        !string.IsNullOrEmpty(apiKey)
        && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(expected));
}
