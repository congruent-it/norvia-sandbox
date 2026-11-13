using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalMasterDataSyncWorker;

/// <summary>What the E3 master-data sync Logic App has as parameters in Azure, given to the worker for the run.</summary>
public sealed class MasterDataSyncWorkerOptions
{
    public const string SectionName = "MasterDataSync";

    /// <summary>The Service Bus topic the master-data sync subscription is on.</summary>
    [Required]
    public string Topic { get; set; } = "master-data-events";

    /// <summary>The E3 Logic App's own subscription, filtered to the master-data entity types it replicates.</summary>
    [Required]
    public string Subscription { get; set; } = "master-data-sync";

    [Required]
    public Uri? TmsBaseAddress { get; set; }

    /// <summary>The E3 Logic App's token for the TMS API, with the role <c>MasterData.Replace</c>.</summary>
    [Required]
    [ValidateObjectMembers]
    public LocalTokenOptions TmsTokens { get; set; } = new();

    /// <summary>The bounded exponential retry policy, with the numbers of the workflow's <c>retryPolicy</c>.</summary>
    public RetryOptions Retry { get; set; } = new();
}
