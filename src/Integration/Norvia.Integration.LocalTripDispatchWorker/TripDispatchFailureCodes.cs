namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>
/// The failure codes of the Trip dispatch flow, under the names the workflow's tracked properties and its dead-letter
/// reason use (coding guidelines §7).
/// </summary>
internal static class TripDispatchFailureCodes
{
    public const string Unreadable = "TripDispatchUnreadable";
    public const string DriverAppUnavailable = "DriverAppUnavailable";
    public const string DriverAppRefused = "DriverAppRefused";
    public const string TmsUnavailable = "TmsUnavailable";
    public const string TmsCallRefused = "TmsCallRefused";
}
