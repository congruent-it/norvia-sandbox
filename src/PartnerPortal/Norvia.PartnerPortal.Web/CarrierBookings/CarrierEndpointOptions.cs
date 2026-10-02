namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>
/// The credential each Carrier Persona's booking endpoint asks of its caller, by the Carrier's ID at Norvia. In Azure
/// the booking Logic App reads the same secrets from Key Vault; locally the AppHost gives both the same one for the run.
/// </summary>
public sealed class CarrierEndpointOptions
{
    public const string SectionName = "CarrierEndpoint";

    public Dictionary<string, string> ApiKeys { get; set; } = [];
}
