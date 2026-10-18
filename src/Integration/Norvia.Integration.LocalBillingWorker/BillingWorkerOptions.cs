using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalBillingWorker;

/// <summary>What the Billing Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class BillingWorkerOptions
{
    public const string SectionName = "Billing";

    /// <summary>The Service Bus topic the Billing subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "transport-order-events";

    /// <summary>The Billing Logic App's own subscription, filtered to <c>TransportOrderBillable</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "billing";

    [Required]
    public Uri? ErpBaseAddress { get; set; }

    /// <summary>The Billing Logic App's token for the ERP billing API, with the role <c>BillableLines.Record</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions ErpTokens { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}
