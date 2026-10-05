using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTrackingWorker;

/// <summary>What the Carrier-tracking Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class TrackingWorkerOptions
{
    public const string SectionName = "Tracking";

    /// <summary>The Service Bus queue the Carrier-tracking Logic App's trigger reads.</summary>
    [Required]
    public string Queue { get; set; } = "carrier-tracking";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Carrier-tracking Logic App's token for the TMS, with the role <c>TrackingEvents.Record</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();
}
