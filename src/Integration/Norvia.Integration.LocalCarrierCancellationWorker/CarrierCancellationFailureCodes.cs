namespace Norvia.Integration.LocalCarrierCancellationWorker;

/// <summary>
/// The failure codes of the Carrier cancellation flow, under the names the workflow's tracked properties and its dead-letter
/// reason use (coding guidelines §7).
/// </summary>
internal static class CarrierCancellationFailureCodes
{
    public const string Unreadable = "CarrierCancellationUnreadable";
    public const string CarrierUnavailable = "CarrierUnavailable";
    public const string CarrierRefused = "CarrierRefused";
    public const string TmsUnavailable = "TmsUnavailable";
    public const string TmsCallRefused = "TmsCallRefused";
}
