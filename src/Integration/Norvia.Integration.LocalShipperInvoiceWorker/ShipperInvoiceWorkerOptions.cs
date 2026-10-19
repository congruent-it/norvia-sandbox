using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalShipperInvoiceWorker;

/// <summary>What the Shipper invoice Logic App has as parameters and Key Vault secrets in Azure, given to the worker for the run.</summary>
public sealed class ShipperInvoiceWorkerOptions
{
    public const string SectionName = "ShipperInvoice";

    /// <summary>The Service Bus topic the Shipper invoice subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "invoicing-events";

    /// <summary>The Shipper invoice Logic App's own subscription, filtered to <c>ShipperInvoiceIssued</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "shipper-invoice";

    [Required]
    public Uri? ErpBaseAddress { get; set; }

    /// <summary>The Shipper invoice Logic App's token for the ERP API, with the role <c>ShipperInvoices.Report</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions ErpTokens { get; set; } = new();

    /// <summary>Each Shipper Persona's invoice endpoint and credential, by Shipper ID.</summary>
    public Dictionary<string, ShipperEndpointOptions> Shippers { get; set; } = [];

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}

/// <summary>A Shipper Persona's invoice endpoint in the Partner portal, and the credential Key Vault holds in Azure.</summary>
public sealed class ShipperEndpointOptions
{
    [Required]
    public Uri? Address { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class RetryOptions
{
    /// <summary>How many times a send is retried after the first attempt.</summary>
    [Range(0, 10)]
    public int Count { get; set; } = 3;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromSeconds(20);
}
