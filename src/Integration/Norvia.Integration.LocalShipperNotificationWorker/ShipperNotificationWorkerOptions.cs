using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalShipperNotificationWorker;

/// <summary>What the Shipper notification Logic App has as parameters and Key Vault secrets in Azure, given to the worker for the run.</summary>
public sealed class ShipperNotificationWorkerOptions
{
    public const string SectionName = "ShipperNotification";

    /// <summary>The Service Bus topic the Shipper notification subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "shipment-events";

    /// <summary>The Shipper notification Logic App's own subscription, filtered to <c>ShipperNotificationRequested</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "shipper-notification";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Shipper notification Logic App's token for the TMS, with the role <c>ShipperNotifications.Report</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>Each Shipper Persona's notification endpoint and credential, by Shipper ID.</summary>
    public Dictionary<string, ShipperEndpointOptions> Shippers { get; set; } = [];

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}

/// <summary>A Shipper Persona's notification endpoint in the Partner portal, and the credential Key Vault holds in Azure.</summary>
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
