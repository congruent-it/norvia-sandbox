using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceWorker;

/// <summary>What the Carrier invoice Logic App has as parameters and identities in Azure, given to the worker for the run.</summary>
public sealed class CarrierInvoiceWorkerOptions
{
    public const string SectionName = "CarrierInvoice";

    [Required]
    public Uri? ErpBaseAddress { get; set; }

    /// <summary>The Carrier invoice Logic App's token for the ERP API, with the role <c>CarrierInvoices.Receive</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions ErpTokens { get; set; } = new();

    /// <summary>
    /// The tokens the workflow's trigger accepts: <see cref="LocalTokenOptions.ClientId"/> is the one caller, API Management, which the
    /// workflow's first step checks in Azure.
    /// </summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions Trigger { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}

public sealed class RetryOptions
{
    /// <summary>How many times the call is retried after the first attempt.</summary>
    [Range(0, 10)]
    public int Count { get; set; } = 3;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromSeconds(20);
}
