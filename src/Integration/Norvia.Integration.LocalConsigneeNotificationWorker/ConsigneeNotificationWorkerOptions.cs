using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalConsigneeNotificationWorker;

/// <summary>What the Consignee notification Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class ConsigneeNotificationWorkerOptions
{
    public const string SectionName = "ConsigneeNotification";

    /// <summary>The Service Bus topic the Consignee notification subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "shipment-events";

    /// <summary>The Consignee notification Logic App's own subscription, filtered to <c>ConsigneeNotificationRequested</c>.</summary>
    [Required]
    public string Subscription { get; set; } = "consignee-notification";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The Consignee notification Logic App's token for the TMS, with the role <c>ConsigneeNotifications.Read</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>The Partner portal's address, which the link in the email is on (<c>/track/{code}</c>).</summary>
    [Required]
    public Uri? PartnerPortalBaseAddress { get; set; }

    /// <summary>Where the email goes locally, standing in for the Azure Communication Services email resource.</summary>
    public MailOptions Mail { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}

/// <summary>
/// Where the worker's email goes: an SMTP server such as the AppHost's mail catcher when <see cref="Host"/> is set, otherwise .eml files in
/// <see cref="PickupDirectory"/>. Nothing leaves the machine.
/// </summary>
public sealed class MailOptions
{
    /// <summary>The sender address of every email, standing in for the email resource's sender in Azure.</summary>
    [Required]
    public string SenderAddress { get; set; } = "tracking@norvia.example";

    /// <summary>The SMTP server's host; empty to write files instead.</summary>
    public string? Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Where the .eml files go when no <see cref="Host"/> is set; the system's temp folder when empty too.</summary>
    public string? PickupDirectory { get; set; }
}

public sealed class RetryOptions
{
    /// <summary>How many times a call is retried after the first attempt.</summary>
    [Range(0, 10)]
    public int Count { get; set; } = 3;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromSeconds(20);
}
