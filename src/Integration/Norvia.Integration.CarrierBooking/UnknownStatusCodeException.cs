namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// A Carrier's tracking message has a status code that isn't in the shared code list. The Carrier-tracking Logic App's
/// run fails, so the message goes to the tracking queue's dead-letter queue (P5).
/// </summary>
public sealed class UnknownStatusCodeException : Exception
{
    public UnknownStatusCodeException(string? statusCode)
        : base(statusCode is null
            ? "The tracking message has no readable status code: it isn't a JSON object with a statusCode."
            : $"The status code '{statusCode}' isn't in the Carrier tracking code list.")
    {
        StatusCode = statusCode;
    }

    public string? StatusCode { get; }
}
