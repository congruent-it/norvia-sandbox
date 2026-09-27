namespace Norvia.Tms.Web.TmsApi;

/// <summary>Tokens as the AppHost's local key signs them where Entra would.</summary>
public sealed class LocalTmsApiTokenOptions
{
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The TMS API, as it knows itself locally.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>The TMS UI's local client ID, which stands in for its managed identity.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(SigningKey);
}
