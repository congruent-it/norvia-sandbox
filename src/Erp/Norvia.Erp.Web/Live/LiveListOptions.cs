using System.ComponentModel.DataAnnotations;

namespace Norvia.Erp.Web.Live;

public sealed class LiveListOptions
{
    public const string SectionName = "LiveList";

    /// <summary>
    /// How often an open list reads again, so a Billable line recorded meanwhile appears without a refresh. Short enough that the
    /// Billing Logic App's call and the ERP screen side by side seem to update in the same moment.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.250", "00:01:00")]
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(1);
}
