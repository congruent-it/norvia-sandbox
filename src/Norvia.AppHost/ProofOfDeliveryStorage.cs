using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Storage;

namespace Norvia.AppHost;

/// <summary>The TMS's blob container for Proof of delivery files, and what the storage account does with the files in it.</summary>
internal static class ProofOfDeliveryStorage
{
    /// <summary>The container's name. The TMS's blob adapter names the same one.</summary>
    public const string ContainerName = "proofs-of-delivery";

    /// <summary>How long a file stays in the hot tier before the lifecycle rule moves it to cool.</summary>
    public const int HotDays = 30;

    /// <summary>Adds the lifecycle rule that moves the container's blobs to cool after <see cref="HotDays"/> days, and does nothing more.</summary>
    public static void AddLifecycleRule(AzureResourceInfrastructure infrastructure)
    {
        var account = infrastructure.GetProvisionableResources().OfType<StorageAccount>().Single();
        var policy = new StorageAccountManagementPolicy("proofOfDeliveryLifecycle")
        {
            Parent = account,
            Rules =
            [
                new ManagementPolicyRule
                {
                    Name = "proof-of-delivery-files-to-cool",
                    IsEnabled = true,
                    RuleType = ManagementPolicyRuleType.Lifecycle,
                    Definition = new ManagementPolicyDefinition
                    {
                        Filters = new ManagementPolicyFilter
                        {
                            BlobTypes = ["blockBlob"],
                            PrefixMatch = [$"{ContainerName}/"],
                        },
                        Actions = new ManagementPolicyAction
                        {
                            BaseBlob = new ManagementPolicyBaseBlob
                            {
                                TierToCool = new DateAfterModification { DaysAfterModificationGreaterThan = HotDays },
                            },
                        },
                    },
                },
            ],
        };
        infrastructure.Add(policy);
    }
}
