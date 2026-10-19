namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// What the ERP billing API's status for a Carrier invoice means for the tracking contract's <c>outcome</c> and <c>failureCode</c>
/// (coding guidelines §7), and why an invoice it couldn't take in was not received. One place, for the Logic App's relay and for the
/// workflow's tracked properties to be tested against.
/// </summary>
public static class ErpInvoiceOutcome
{
    public const string Succeeded = "Succeeded";
    public const string Refused = "Refused";
    public const string Failed = "Failed";

    /// <param name="erpStatusCode">The status the ERP answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <returns>The outcome and, when there is one, its failure code.</returns>
    public static (string Outcome, string? FailureCode) Of(int erpStatusCode) => erpStatusCode switch
    {
        200 or 201 => (Succeeded, null),
        400 => (Refused, "CarrierInvoiceRefused"),
        409 => (Refused, "InvoiceNumberUsed"),
        422 => (Refused, "CarrierNotFound"),
        _ => (Failed, WhyNotReceived(erpStatusCode)),
    };

    /// <summary>
    /// A server error, a timeout, throttling or no answer means the ERP couldn't be reached. Any other client error means the integration
    /// layer sent something the ERP won't take, which is not the Carrier's fault either.
    /// </summary>
    public static string WhyNotReceived(int erpStatusCode) =>
        erpStatusCode is >= 400 and < 500 and not 408 and not 429 ? "ErpCallRefused" : "ErpUnavailable";
}
