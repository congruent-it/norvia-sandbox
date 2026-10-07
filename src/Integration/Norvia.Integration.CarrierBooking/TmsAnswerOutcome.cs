namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// What the TMS's status for a Carrier's answer means for the tracking contract's <c>outcome</c> and <c>failureCode</c>
/// (coding guidelines §7), and why an answer it couldn't take in was not received. One place, for the Logic App's
/// relay and for the local stand-in that plays it, so a local trail reads as one in Azure.
/// </summary>
public static class TmsAnswerOutcome
{
    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Failed = "Failed";

    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <returns>The outcome and, when there is one, its failure code.</returns>
    public static (string Outcome, string? FailureCode) Of(int tmsStatusCode) => tmsStatusCode switch
    {
        200 => (Succeeded, null),
        403 => (Refused, "CarrierNotAllowed"),
        404 => (Refused, "BookingNotFound"),
        409 => (Refused, "BookingNotOpen"),
        422 => (Refused, "ReferenceInUse"),
        _ => (Failed, WhyNotReceived(tmsStatusCode)),
    };

    /// <summary>
    /// A server error, a timeout, throttling or no answer means the TMS couldn't be reached. Any other client error means
    /// the integration layer sent something the TMS won't take, which is not the Carrier's fault either.
    /// </summary>
    public static string WhyNotReceived(int tmsStatusCode) =>
        tmsStatusCode is >= 400 and < 500 and not 408 and not 429 ? "TmsCallRefused" : "TmsUnavailable";
}
