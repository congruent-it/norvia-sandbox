using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.TmsApi;

/// <summary>
/// Sends the TMS UI's commands to the TMS API as the TMS UI's own identity. The standard resilience handler retries a
/// call, so each re-rate goes under an idempotency key of its own that every retry of it repeats.
/// </summary>
internal sealed class TmsApiClient(HttpClient http, ITmsApiTokens tokens) : ITransportOrderCommands
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/re-rate", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        request.Headers.Add(IdempotencyKeyHeader, Guid.NewGuid().ToString());

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var rated = await response.Content.ReadFromJsonAsync<ReRated>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a re-rate without a body.");
                return rated is { RatingResult: RatingResult.RatingFailed, RatingFailure: { } failure }
                    ? new ReRateAnswer.FailedAgain(failure)
                    : new ReRateAnswer.Rated();
            case HttpStatusCode.Conflict:
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, cancellationToken);
                return new ReRateAnswer.Refused(problem?.Detail ?? $"Transport Order {transportOrderNumber} can't be re-rated.");
            case HttpStatusCode.NotFound:
                return new ReRateAnswer.Refused($"There is no Transport Order {transportOrderNumber}.");
            default:
                response.EnsureSuccessStatusCode();
                throw new HttpRequestException($"The TMS API answered a re-rate with {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    // What the TMS API answers a re-rate, as far as the TMS UI reads it.
    private sealed record ReRated(RatingResult RatingResult, RatingFailureView? RatingFailure);
}
