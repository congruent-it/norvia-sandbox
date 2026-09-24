using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Web.TransportOrders;

public sealed class LiveListOptions
{
    public const string SectionName = "LiveList";

    /// <summary>
    /// How often an open list reads the Transport Orders again, so a newly accepted one appears without a refresh. Short
    /// enough that a booking and the TMS screen side by side seem to update in the same moment.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.250", "00:01:00")]
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(1);
}
