namespace Norvia.Integration.LocalMasterDataSyncWorker;

/// <summary>
/// The failure codes of the E3 flow, under the names the workflow's tracked properties and its dead-letter reason use
/// (coding guidelines §7).
/// </summary>
internal static class MasterDataSyncFailureCodes
{
    public const string Unreadable = "MasterDataUnreadable";
    public const string TmsUnavailable = "TmsUnavailable";
    public const string TmsCallRefused = "TmsCallRefused";
}
