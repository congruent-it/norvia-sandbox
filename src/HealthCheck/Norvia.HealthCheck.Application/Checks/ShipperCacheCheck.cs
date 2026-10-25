using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>API Management does not cache the Shipper tracking answers for longer than five minutes.</summary>
public sealed class ShipperCacheCheck : Check
{
    public const string CheckId = "APIM-CACHE";

    /// <summary>The longest an answer may be cached before a Shipper can be misled by it.</summary>
    public const int MaxCacheSeconds = 300;

    public override string Id => CheckId;

    public override string Rule => "API Management does not cache the Shipper tracking answers for longer than five minutes.";

    public override string Title => "API Management caches Shipper tracking answers for long";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "shipperTrackingCached";

    public override string Fix =>
        "Set shipperTrackingCached to false and provision the estate: partner-api.bicep then fills the policies with no cache.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        var seconds = estate.ApiManagement.ShipperTrackingCacheSeconds;
        return seconds > MaxCacheSeconds
            ? [new Evidence("Shipper tracking API", $"Answers are cached for {seconds} seconds, so a Shipper can see an old status after the TMS changed.")]
            : [];
    }
}
