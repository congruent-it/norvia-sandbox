using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The Shipper tracking operations have a rate limit.</summary>
public sealed class ShipperRateLimitCheck : Check
{
    public const string CheckId = "APIM-RATE-LIMIT";

    public override string Id => CheckId;

    public override string Rule => "The Shipper tracking operations have a rate limit.";

    public override string Title => "The Shipper product has no rate limit";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "noShipperRateLimit";

    public override string Fix =>
        "Set noShipperRateLimit to false and provision the estate: partner-api.bicep then fills the policies with shipper-rate-limit.fragment.xml.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.ApiManagement.ShipperRateLimit == false
            ? [new Evidence("Shipper tracking API", "The policies have no rate limit, so one Shipper can use all of the TMS's capacity.")]
            : [];
    }
}
