namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>
/// The failure codes of the Driver reports flow that the integration layer gives, under the names the workflow's tracked properties and its dead-letter
/// reason use (coding guidelines §7). A refusal by the TMS keeps the TMS's own code.
/// </summary>
internal static class DriverReportsFailureCodes
{
    public const string Unreadable = "DriverReportUnreadable";
    public const string ProofOfDeliveryFilesGone = "ProofOfDeliveryFilesGone";
    public const string TmsUnavailable = "TmsUnavailable";
    public const string TmsCallRefused = "TmsCallRefused";
    public const string DriverAppUnavailable = "DriverAppUnavailable";
    public const string DriverAppRefused = "DriverAppRefused";
}
