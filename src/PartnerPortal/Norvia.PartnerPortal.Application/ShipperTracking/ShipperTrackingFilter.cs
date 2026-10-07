namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>What narrows and pages the list of Transport Orders. Every part is optional, and the first page is page 1.</summary>
/// <param name="Status">Only Transport Orders with this Tracking status.</param>
/// <param name="CollectionFrom">Only Transport Orders whose collection window opens on or after this date.</param>
/// <param name="CollectionTo">Only Transport Orders whose collection window opens on or before this date.</param>
public sealed record ShipperTrackingFilter(ShipperTrackingStatus? Status = null, DateOnly? CollectionFrom = null, DateOnly? CollectionTo = null, int Page = 1)
{
    /// <summary>How many Transport Orders a page holds. The API's own default; a full page may have another after it.</summary>
    public const int PageSize = 25;
}
