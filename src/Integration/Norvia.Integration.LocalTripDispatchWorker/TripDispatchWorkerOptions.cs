using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>What the Trip dispatch Logic App has as parameters and Key Vault secrets in Azure, given to the worker for the run.</summary>
public sealed class TripDispatchWorkerOptions
{
    public const string SectionName = "TripDispatch";

    /// <summary>The Service Bus topic the Trip dispatch subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "trip-events";

    /// <summary>The Trip dispatch Logic App's own subscription, filtered to <c>TripDispatched</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "trip-dispatch";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Trip dispatch Logic App's token for the TMS, with the role <c>Trips.ReportDelivery</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>The Driver app's Trip endpoint and credential.</summary>
    [Required]
    [ValidateObjectMembers]
    public DriverAppEndpointOptions DriverApp { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}
