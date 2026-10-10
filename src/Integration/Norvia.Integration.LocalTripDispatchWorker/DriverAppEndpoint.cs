using System.Text;

namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>
/// Sends a Trip version to the Driver app's Trip endpoint, with the credential and the action's correlation ID. It doesn't
/// retry: the flow does, under the workflow's policy.
/// </summary>
internal sealed class DriverAppEndpoint(HttpClient http)
{
    private const int MaximumMessageLength = 500;

    /// <summary>The endpoint's status and what it said, or 0 and the technical cause when it didn't answer.</summary>
    public async Task<DriverAppAnswer> SendAsync(DriverAppEndpointOptions endpoint, string trip, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Address)
            {
                Content = new StringContent(trip, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Add("X-Api-Key", endpoint.ApiKey);

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new((int)response.StatusCode, Shorten(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? string.Empty : body));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // A timeout is a canceled request the caller didn't cancel.
            return new(0, Shorten(exception is TaskCanceledException ? "The Driver app timed out." : exception.Message));
        }
    }

    private static string Shorten(string message) =>
        message.Length <= MaximumMessageLength ? message : message[..MaximumMessageLength];
}

/// <param name="StatusCode">The HTTP status, or 0 when the endpoint didn't answer.</param>
/// <param name="Message">What the endpoint said, or the technical cause.</param>
internal sealed record DriverAppAnswer(int StatusCode, string Message)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;

    /// <summary>A server error, a timeout or no answer, as the workflow's policy retries; a client error is the Driver app refusing the Trip.</summary>
    public bool IsRetryable => StatusCode is 0 or 408 or 429 or >= 500;
}
