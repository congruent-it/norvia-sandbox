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

    /// <summary>
    /// How long a file is kept before the lifecycle rule deletes it when <c>proofRetentionDays</c> is not set: 7 years, which covers the CMR
    /// convention's claim limits (1 year, 3 for wilful misconduct) and the 5 to 10 years of EU accounting rules a freight invoice falls under.
    /// A sandbox default, not legal advice.
    /// </summary>
    public const int DefaultRetentionDays = 2555;

    /// <summary>
    /// The retention in days: <paramref name="configured"/> is the <c>proofRetentionDays</c> parameter (an environment variable or azd
    /// setting); unset, empty or not a positive whole number is <see cref="DefaultRetentionDays"/>. A short value shows a deletion.
    /// </summary>
    public static int RetentionDays(string? configured) =>
        int.TryParse(configured, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days) && days > 0
            ? days
            : DefaultRetentionDays;

    /// <summary>
    /// Whether the as-found toggle <c>proofFilesStayHot</c> is set (docs/operations/as-found.md). <paramref name="asFound"/> is the
    /// <c>ESTATE_ASFOUND</c> object infra/as-found.ps1 stores in the azd environment; unset, empty or without the toggle is the fixed state.
    /// </summary>
    public static bool StayHot(string? asFound) => AsFound.IsSet(asFound, "proofFilesStayHot");

    /// <summary>
    /// Whether the as-found toggle <c>proofFilesNeverDeleted</c> is set: the retention rule is left out, so files are kept for good.
    /// Read like <see cref="StayHot"/>.
    /// </summary>
    public static bool NeverDeleted(string? asFound) => AsFound.IsSet(asFound, "proofFilesNeverDeleted");

    /// <summary>
    /// Adds the lifecycle rules: one moves the container's blobs to cool after <see cref="HotDays"/> days, and one deletes them after
    /// <paramref name="retentionDays"/> (<see cref="RetentionDays"/>) unless <paramref name="keepForever"/>. Files are written once, so
    /// days since modification are days since creation, and the clock is the same for current and superseded files.
    /// </summary>
    public static void AddLifecycleRule(AzureResourceInfrastructure infrastructure, int retentionDays, bool keepForever)
    {
        var account = infrastructure.GetProvisionableResources().OfType<StorageAccount>().Single();
        var policy = new StorageAccountManagementPolicy("proofOfDeliveryLifecycle")
        {
            Parent = account,
            Rules = [.. Rules(retentionDays, keepForever)],
        };
        infrastructure.Add(policy);
    }

    private static IEnumerable<ManagementPolicyRule> Rules(int retentionDays, bool keepForever)
    {
        yield return Rule("proof-of-delivery-files-to-cool", new ManagementPolicyBaseBlob
        {
            TierToCool = new DateAfterModification { DaysAfterModificationGreaterThan = HotDays },
        });

        if (!keepForever)
        {
            yield return Rule("proof-of-delivery-files-expire", new ManagementPolicyBaseBlob
            {
                Delete = new DateAfterModification { DaysAfterModificationGreaterThan = retentionDays },
            });
        }
    }

    private static ManagementPolicyRule Rule(string name, ManagementPolicyBaseBlob baseBlob) =>
        new()
        {
            Name = name,
            IsEnabled = true,
            RuleType = ManagementPolicyRuleType.Lifecycle,
            Definition = new ManagementPolicyDefinition
            {
                Filters = new ManagementPolicyFilter
                {
                    BlobTypes = ["blockBlob"],
                    PrefixMatch = [$"{ContainerName}/"],
                },
                Actions = new ManagementPolicyAction { BaseBlob = baseBlob },
            },
        };
}
