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

    /// <summary>
    /// How long a message is held before it is abandoned for the queue to deliver again. None by default, as the stand-in has no trigger polling interval: the queue delivers it again at once, so
    /// a TMS that is down for a moment uses up the queue's deliveries in a fraction of a second. A test that wants a message to outlive a fault of seconds sets it.
    /// </summary>
    public TimeSpan RedeliveryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>The Carrier-tracking Logic App's token for the TMS, with the role <c>TrackingEvents.Record</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();
}
