namespace Norvia.DriverApp.Application;

/// <summary>The failure codes the Driver app gives a Stop report it does not take, on the trail of the Driver's action (coding guidelines §7).</summary>
internal static class TrackingFailureCodes
{
    public const string StopNotFound = "StopNotFound";
    public const string StopReportInvalid = "StopReportInvalid";
    public const string ProofOfDeliveryInvalid = "ProofOfDeliveryInvalid";
    public const string TripNotFound = "TripNotFound";
    public const string TripNotUnderWay = "TripNotUnderWay";
    public const string StopsOpen = "StopsOpen";
    public const string StopNotAvailable = "StopNotAvailable";
}
