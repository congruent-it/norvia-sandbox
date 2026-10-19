namespace Norvia.Simulator.Infrastructure.ShipperInbox;

/// <summary>
/// Where the Partner portal is and the credential each Shipper Persona's invoice endpoint asks, which its inbox listing asks too. Locally the AppHost gives the
/// portal and the Simulator the same keys for the run; in Azure they are the Shippers' endpoint secrets in the portal's vault. Without the portal's address, or
/// a Shipper's key, the Simulator can't read that inbox and says so in the log.
/// </summary>
public sealed class ShipperInboxOptions
{
    public const string SectionName = "PartnerPortal";

    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>The Partner portal's address, such as <c>http://localhost:5400</c>.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The Shipper's invoice endpoint key, by the Shipper's ID.</summary>
    public Dictionary<string, string> ShipperEndpointKeys { get; set; } = [];
}
