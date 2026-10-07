using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierCancellationWorker;

/// <summary>What the Carrier cancellation Logic App has as parameters and Key Vault secrets in Azure, given to the worker for the run.</summary>
public sealed class CarrierCancellationWorkerOptions
{
    public const string SectionName = "CarrierCancellation";

    /// <summary>The Service Bus topic the Carrier cancellation subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "shipment-events";

    /// <summary>The Carrier cancellation Logic App's own subscription, filtered to <c>CarrierBookingCancelled</c> or <c>CarrierBookingWithdrawn</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "carrier-cancellation";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Carrier cancellation Logic App's token for the TMS, with the role <c>CarrierBookings.Report</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>Each Carrier Persona's cancellation endpoint and credential, by Carrier ID.</summary>
    public Dictionary<string, CarrierEndpointOptions> Carriers { get; set; } = [];

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}
