namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// A Carrier's tracking message has a status code that isn't in the shared code list. The Carrier-tracking Logic App's
/// run fails, so the message goes to the tracking queue's dead-letter queue (P5).
/// </summary>
public sealed class UnknownStatusCodeException : Exception
{
    public UnknownStatusCodeException(string? statusCode)
        : base($"The status code '{statusCode}' isn't in the Carrier tracking code list.")
    {
        StatusCode = statusCode;
    }

    public string? StatusCode { get; }
}
