using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceOutcomeWorker;

/// <summary>What the Carrier invoice outcome Logic App has as parameters and Key Vault secrets in Azure, given to the worker for the run.</summary>
public sealed class CarrierInvoiceOutcomeWorkerOptions
{
    public const string SectionName = "CarrierInvoiceOutcome";

    /// <summary>The Service Bus topic the Carrier invoice outcome subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "invoicing-events";

    /// <summary>The Carrier invoice outcome Logic App's own subscription, filtered to <c>CarrierInvoiceApproved</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "carrier-invoice-outcome";

    [Required]
    public Uri? ErpBaseAddress { get; set; }

    /// <summary>The Carrier invoice outcome Logic App's token for the ERP API, with the role <c>CarrierInvoices.Report</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions ErpTokens { get; set; } = new();

    /// <summary>Each Carrier Persona's invoice outcome endpoint and credential, by Carrier ID.</summary>
    public Dictionary<string, CarrierEndpointOptions> Carriers { get; set; } = [];

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}

/// <summary>A Carrier Persona's invoice outcome endpoint in the Partner portal, and the credential Key Vault holds in Azure.</summary>
public sealed class CarrierEndpointOptions
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
