namespace Norvia.Integration.LocalCarrierInvoiceWorker;

/// <summary>How the flow reads an HTTP status, as the workflow's retry policy does. Zero is no answer.</summary>
internal static class HttpStatus
{
    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    /// <summary>No answer, a timeout, 429 or a server error: what the workflow's retry policy retries.</summary>
    public static bool IsTransient(int statusCode) => statusCode is 0 or 408 or 429 or >= 500;
}
