using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.TmsApi;

/// <summary>
/// Sends the TMS UI's commands to the TMS API as the TMS UI's own identity. The standard resilience handler retries a
/// call, so each re-rate and each assignment goes under an idempotency key of its own that every retry of it repeats.
/// </summary>
internal sealed class TmsApiClient(HttpClient http, ITmsApiTokens tokens) : ITransportOrderCommands
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        using var request = await CommandAsync($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/re-rate", cancellationToken);

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
                return new ReRateAnswer.Refused(await ReasonAsync(response, $"Transport Order {transportOrderNumber} can't be re-rated.", cancellationToken));
            case HttpStatusCode.NotFound:
                return new ReRateAnswer.Refused($"There is no Transport Order {transportOrderNumber}.");
            default:
                throw Unexpected(response, "a re-rate");
        }
    }

    public async Task<AssignAnswer> AssignAsync(string transportOrderNumber, string? carrierId, CancellationToken cancellationToken)
    {
        using var request = await CommandAsync($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/assign", cancellationToken);
        request.Content = JsonContent.Create(new Assignment(carrierId is null ? AssignedTo.OwnFleet : AssignedTo.Carrier, carrierId), options: Json);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            // Created for the assignment, OK for a retry of it that the TMS answers as a repeat.
            case HttpStatusCode.Created or HttpStatusCode.OK:
                var assigned = await response.Content.ReadFromJsonAsync<Assigned>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered an assignment without a body.");
                return new AssignAnswer.Planned(assigned.ShipmentNumber);
            // A refused assignment: its problem's detail is the refusal's readable message.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                return new AssignAnswer.Refused(await ReasonAsync(response, $"Transport Order {transportOrderNumber} can't be assigned.", cancellationToken));
            default:
                throw Unexpected(response, "an assignment");
        }
    }

    // A command as the TMS UI's own identity, under a new idempotency key that every retry of it repeats.
    private async Task<HttpRequestMessage> CommandAsync(string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        request.Headers.Add(IdempotencyKeyHeader, Guid.NewGuid().ToString());
        return request;
    }

    private static async Task<string> ReasonAsync(HttpResponseMessage response, string otherwise, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, cancellationToken);
        return problem?.Detail ?? otherwise;
    }

    private static HttpRequestException Unexpected(HttpResponseMessage response, string command)
    {
        response.EnsureSuccessStatusCode();
        return new HttpRequestException($"The TMS API answered {command} with {(int)response.StatusCode}.", null, response.StatusCode);
    }

    // What the TMS API answers a re-rate, as far as the TMS UI reads it.
    private sealed record ReRated(RatingResult RatingResult, RatingFailureView? RatingFailure);

    // What the TMS UI asks the TMS API to assign a Transport Order to.
    private sealed record Assignment(AssignedTo AssignedTo, string? CarrierId);

    // What the TMS API answers an assignment, as far as the TMS UI reads it.
    private sealed record Assigned(string ShipmentNumber);
}
