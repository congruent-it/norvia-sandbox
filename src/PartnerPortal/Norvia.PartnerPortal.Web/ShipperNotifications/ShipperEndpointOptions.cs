namespace Norvia.PartnerPortal.Web.ShipperNotifications;

/// <summary>
/// The credential each Shipper Persona's notification endpoint asks of its caller, by the Shipper's ID at Norvia. In Azure
/// the Shipper notification Logic App reads the same secrets from Key Vault; locally the AppHost gives both the same one
/// for the run.
/// </summary>
public sealed class ShipperEndpointOptions
{
    public const string SectionName = "ShipperEndpoint";

    public Dictionary<string, string> ApiKeys { get; set; } = [];
}
