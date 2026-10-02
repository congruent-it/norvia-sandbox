using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.TmsApi;

/// <summary>
/// Sends the TMS UI's commands to the TMS API as the TMS UI's own identity. The standard resilience handler retries a
/// call, so each re-rate, assignment, reassignment and release goes under an idempotency key of its own that every retry
/// of it repeats. Each command goes in a span of its own, which the Integration console names the TMS UI's hop after,
/// and its answer is logged with the tracking contract's keys (coding guidelines §7), on the Message trail the answer
/// names.
/// </summary>
internal sealed partial class TmsApiClient(HttpClient http, ITmsApiTokens tokens, TmsUiActivities activities, ILogger<TmsApiClient> logger)
    : ITransportOrderCommands
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Re-rate Transport Order", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/re-rate", cancellationToken);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var rated = await response.Content.ReadFromJsonAsync<ReRated>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a re-rate without a body.");
                // A re-rate continues the trail of the order's intake, and Rating failed is its answer, not a failed flow.
                using (Track(Flows.TransportOrderRating, transportOrderNumber, TrackingKeys.Succeeded, rated.RatingFailure?.Code, rated.CorrelationId, rated.ShipperId))
                {
                    LogReRated(transportOrderNumber, rated.RatingResult);
                }

                return rated is { RatingResult: RatingResult.RatingFailed, RatingFailure: { } failure }
                    ? new ReRateAnswer.FailedAgain(failure)
                    : new ReRateAnswer.Rated();
            case HttpStatusCode.Conflict:
                var problem = await response.Content.ReadFromJsonAsync<Refusal>(Json, cancellationToken);
                return Refused(new ReRateAnswer.Refused(problem?.Detail ?? $"Transport Order {transportOrderNumber} can't be re-rated."), problem?.CorrelationId);
            case HttpStatusCode.NotFound:
                return Refused(new ReRateAnswer.Refused($"There is no Transport Order {transportOrderNumber}."), correlationId: null);
            default:
                throw Unexpected(response, "a re-rate");
        }

        ReRateAnswer Refused(ReRateAnswer.Refused refused, string? correlationId)
        {
            using (Track(Flows.TransportOrderRating, transportOrderNumber, TrackingKeys.Refused, correlationId: correlationId))
            {
                LogRefused(TmsCommand.ReRate, transportOrderNumber, refused.Reason);
            }

            return refused;
        }
    }

    public async Task<AssignAnswer> AssignAsync(string transportOrderNumber, string? carrierId, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Assign Transport Order", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/assign", cancellationToken);
        request.Content = JsonContent.Create(new Assignment(carrierId is null ? AssignedTo.OwnFleet : AssignedTo.Carrier, carrierId), options: Json);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            // Created for the assignment, OK for a retry of it that the TMS answers as a repeat.
            case HttpStatusCode.Created or HttpStatusCode.OK:
                var assigned = await response.Content.ReadFromJsonAsync<Assigned>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered an assignment without a body.");
                using (Track(Flows.TransportPlanning, transportOrderNumber, TrackingKeys.Succeeded, null, assigned.CorrelationId, assigned.ShipperId, assigned.ShipmentNumber))
                {
                    LogAssigned(transportOrderNumber, assigned.ShipmentNumber);
                }

                return new AssignAnswer.Planned(assigned.ShipmentNumber);
            // A refused assignment: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                var problem = await response.Content.ReadFromJsonAsync<Refusal>(Json, cancellationToken);
                var reason = problem?.Detail ?? $"Transport Order {transportOrderNumber} can't be assigned.";
                using (Track(Flows.TransportPlanning, transportOrderNumber, TrackingKeys.Refused, problem?.Code, problem?.CorrelationId))
                {
                    LogRefused(TmsCommand.Assignment, transportOrderNumber, reason);
                }

                return new AssignAnswer.Refused(reason);
            default:
                throw Unexpected(response, "an assignment");
        }
    }

    public async Task<ReassignAnswer> ReassignAsync(string shipmentNumber, string? carrierId, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Reassign Shipment", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/reassign", cancellationToken);
        request.Content = JsonContent.Create(new Reassignment(carrierId is null ? AssignedTo.OwnFleet : AssignedTo.Carrier, carrierId), options: Json);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var reassigned = await response.Content.ReadFromJsonAsync<Reassigned>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a reassignment without a body.");
                using (Track(Flows.TransportPlanning, reassigned.TransportOrderNumber, TrackingKeys.Succeeded, null, reassigned.CorrelationId, reassigned.ShipperId, shipmentNumber))
                {
                    LogReassigned(shipmentNumber);
                }

                return new ReassignAnswer.Reassigned(reassigned.CarriedBy);
            // A refused reassignment: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                return new ReassignAnswer.Refused(
                    await RefuseShipmentCommandAsync(response, TmsCommand.Reassignment, shipmentNumber, "reassigned", cancellationToken));
            default:
                throw Unexpected(response, "a reassignment");
        }
    }

    public async Task<ReleaseAnswer> ReleaseAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Release Shipment", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/release", cancellationToken);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var released = await response.Content.ReadFromJsonAsync<Released>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a release without a body.");
                using (Track(Flows.TransportPlanning, released.TransportOrderNumber, TrackingKeys.Succeeded, null, released.CorrelationId, released.ShipperId, shipmentNumber))
                {
                    LogReleased(shipmentNumber);
                }

                return new ReleaseAnswer.Released();
            // A refused release: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict:
                return new ReleaseAnswer.Refused(
                    await RefuseShipmentCommandAsync(response, TmsCommand.Release, shipmentNumber, "released", cancellationToken));
            default:
                throw Unexpected(response, "a release");
        }
    }

    public async Task<WithdrawAnswer> WithdrawAsync(string carrierBookingNumber, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Withdraw Carrier booking", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/carrier-bookings/{Uri.EscapeDataString(carrierBookingNumber)}/withdraw", cancellationToken);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var withdrawn = await response.Content.ReadFromJsonAsync<Withdrawn>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a withdrawal without a body.");
                using (Track(Flows.TransportPlanning, null, TrackingKeys.Succeeded, null, withdrawn.CorrelationId, null, withdrawn.ShipmentNumber, carrierBookingNumber))
                {
                    LogWithdrawn(carrierBookingNumber);
                }

                return new WithdrawAnswer.Withdrawn();
            // A refused withdrawal: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict:
                var problem = await response.Content.ReadFromJsonAsync<Refusal>(Json, cancellationToken);
                var reason = problem?.Detail ?? $"Carrier booking {carrierBookingNumber} can't be withdrawn.";
                using (Track(Flows.TransportPlanning, null, TrackingKeys.Refused, problem?.Code, problem?.CorrelationId, carrierBookingNumber: carrierBookingNumber))
                {
                    LogWithdrawalRefused(carrierBookingNumber, reason);
                }

                return new WithdrawAnswer.Refused(reason);
            default:
                throw Unexpected(response, "a withdrawal");
        }
    }

    // What a refused reassignment or release shares: reading the problem, tracing the refusal on the trail it names, and
    // answering with its readable reason.
    private async Task<string> RefuseShipmentCommandAsync(
        HttpResponseMessage response, TmsCommand command, string shipmentNumber, string action, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<Refusal>(Json, cancellationToken);
        var reason = problem?.Detail ?? $"Shipment {shipmentNumber} can't be {action}.";
        using (Track(Flows.TransportPlanning, null, TrackingKeys.Refused, problem?.Code, problem?.CorrelationId, shipmentNumber: shipmentNumber))
        {
            LogShipmentRefused(command, shipmentNumber, reason);
        }

        return reason;
    }

    // A command as the TMS UI's own identity, under a new idempotency key that every retry of it repeats.
    private async Task<HttpRequestMessage> CommandAsync(string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
        request.Headers.Add(IdempotencyKeyHeader, Guid.NewGuid().ToString());
        return request;
    }

    /// <summary>
    /// The tracking contract's keys of a command's answer. Only an answer that names a correlation ID puts the TMS UI on
    /// a Message trail, as a re-rate of an order that doesn't exist doesn't. A key that doesn't apply, such as the
    /// Transport Order number on a refused reassignment, is left out, never sent empty.
    /// </summary>
    private IDisposable? Track(
        string flow, string? transportOrderNumber, string outcome, string? failureCode = null, string? correlationId = null, string? partyId = null, string? shipmentNumber = null, string? carrierBookingNumber = null)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.Flow] = flow,
            [TrackingKeys.Outcome] = outcome,
        };
        foreach (var (key, value) in new[]
        {
            (TrackingKeys.TransportOrderNumber, transportOrderNumber),
            (TrackingKeys.FailureCode, failureCode),
            (TrackingKeys.CorrelationId, correlationId),
            (TrackingKeys.PartyId, partyId),
            (TrackingKeys.ShipmentNumber, shipmentNumber),
            (TrackingKeys.CarrierBookingNumber, carrierBookingNumber),
        })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys[key] = value;
            }
        }

        return logger.BeginScope(keys);
    }

    private static HttpRequestException Unexpected(HttpResponseMessage response, string command)
    {
        response.EnsureSuccessStatusCode();
        return new HttpRequestException($"The TMS API answered {command} with {(int)response.StatusCode}.", null, response.StatusCode);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} re-rated: {RatingResult}")]
    private partial void LogReRated(string transportOrderNumber, RatingResult ratingResult);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} assigned, which planned Shipment {ShipmentNumber}")]
    private partial void LogAssigned(string transportOrderNumber, string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Command} of Transport Order {TransportOrderNumber} refused: {Reason}")]
    private partial void LogRefused(TmsCommand command, string transportOrderNumber, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipment {ShipmentNumber} reassigned")]
    private partial void LogReassigned(string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipment {ShipmentNumber} released")]
    private partial void LogReleased(string shipmentNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} withdrawn")]
    private partial void LogWithdrawn(string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Withdrawal of Carrier booking {CarrierBookingNumber} refused: {Reason}")]
    private partial void LogWithdrawalRefused(string carrierBookingNumber, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Command} of Shipment {ShipmentNumber} refused: {Reason}")]
    private partial void LogShipmentRefused(TmsCommand command, string shipmentNumber, string reason);

    private enum TmsCommand
    {
        ReRate,
        Assignment,
        Reassignment,
        Release,
    }

    // What the TMS API answers a re-rate, as far as the TMS UI reads it.
    private sealed record ReRated(RatingResult RatingResult, RatingFailureView? RatingFailure, string? CorrelationId, string? ShipperId);

    // What the TMS API answers a refused command: a problem whose code says which assignment rule an assignment broke,
    // and whose correlation ID names the Message trail the TMS traced the refusal on.
    private sealed record Refusal(string? Detail, string? Code, string? CorrelationId);

    // What the TMS UI asks the TMS API to assign a Transport Order to.
    private sealed record Assignment(AssignedTo AssignedTo, string? CarrierId);

    // What the TMS API answers an assignment, as far as the TMS UI reads it.
    private sealed record Assigned(string ShipmentNumber, string? CorrelationId, string? ShipperId);

    // What the TMS UI asks the TMS API to reassign a Shipment to.
    private sealed record Reassignment(AssignedTo AssignedTo, string? CarrierId);

    // What the TMS API answers a reassignment, as far as the TMS UI reads it.
    private sealed record Reassigned(CarriedByView CarriedBy, string? TransportOrderNumber, string? CorrelationId, string? ShipperId);

    // What the TMS API answers a release, as far as the TMS UI reads it.
    private sealed record Released(string? TransportOrderNumber, string? CorrelationId, string? ShipperId);

    // What the TMS API answers a withdrawal, as far as the TMS UI reads it.
    private sealed record Withdrawn(string? ShipmentNumber, string? CorrelationId);
}
