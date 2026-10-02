using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.LocalBookingWorker;

/// <summary>What the booking Logic App has as parameters and Key Vault secrets in Azure, given to the worker for the run.</summary>
public sealed class BookingWorkerOptions
{
    public const string SectionName = "Booking";

    /// <summary>The Service Bus topic the booking subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "shipment-events";

    /// <summary>The booking Logic App's own subscription, filtered to Carrier Shipments.</summary>
    [Required]
    public string Subscription { get; set; } = "carrier-booking";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The booking Logic App's token for the TMS, with the role <c>CarrierBookings.Report</c>.</summary>
    [Required]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>Each Carrier Persona's booking endpoint and credential, by Carrier ID.</summary>
    public Dictionary<string, CarrierEndpointOptions> Carriers { get; set; } = [];

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}

/// <summary>A Carrier Persona's booking endpoint in the Partner portal, and the credential Key Vault holds in Azure.</summary>
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
