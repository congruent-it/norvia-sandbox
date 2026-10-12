using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>What the Driver reports Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class DriverReportsWorkerOptions
{
    public const string SectionName = "DriverReports";

    /// <summary>The Service Bus topic for Driver app events.</summary>
    [Required]
    public string Topic { get; set; } = "driver-app-events";

    /// <summary>The Driver reports Logic App's own subscription, which uses sessions keyed by Trip number.</summary>
    [Required]
    public string Subscription { get; set; } = "driver-reports";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Driver reports Logic App's token for the TMS, with the roles it needs there (<c>Trips.Start</c>).</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>The Driver app's address and the Driver reports Logic App's token for its outcome endpoint (<c>DriverReports.ReportOutcome</c>).</summary>
    [Required]
    [ValidateObjectMembers]
    public DriverAppOptions DriverApp { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}
