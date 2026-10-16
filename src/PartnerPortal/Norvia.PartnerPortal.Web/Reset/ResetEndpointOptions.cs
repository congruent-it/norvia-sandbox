namespace Norvia.PartnerPortal.Web.Reset;

/// <summary>
/// The credential the Partner portal's reset endpoint asks of its caller, the Simulator. The portal has no token validation of its own, as its
/// other endpoints are called with an API key from Key Vault; the reset takes the same kind of key, and only while the Reset setting is on.
/// </summary>
public sealed class ResetEndpointOptions
{
    public const string SectionName = "ResetEndpoint";

    public string ApiKey { get; set; } = string.Empty;
}
