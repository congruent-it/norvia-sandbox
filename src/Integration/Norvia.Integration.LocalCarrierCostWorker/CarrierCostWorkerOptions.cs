using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierCostWorker;

/// <summary>What the Carrier cost Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class CarrierCostWorkerOptions
{
    public const string SectionName = "CarrierCost";

    /// <summary>The Service Bus topic the Carrier cost subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "shipment-events";

    /// <summary>The Carrier cost Logic App's own subscription, filtered to <c>CarrierCostIncurred</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "carrier-cost";

    [Required]
    public Uri? ErpBaseAddress { get; set; }

    /// <summary>The Carrier cost Logic App's token for the ERP billing API, with the role <c>CarrierCosts.Record</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions ErpTokens { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}
