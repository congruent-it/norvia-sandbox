using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Agents;
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
                using (Track(Flows.TransportOrderRating, TrackingKeys.Succeeded, new() { TransportOrderNumber = transportOrderNumber, FailureCode = rated.RatingFailure?.Code, CorrelationId = rated.CorrelationId, PartyId = rated.ShipperId }))
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
            using (Track(Flows.TransportOrderRating, TrackingKeys.Refused, new() { TransportOrderNumber = transportOrderNumber, CorrelationId = correlationId }))
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
                using (Track(Flows.TransportPlanning, TrackingKeys.Succeeded, new() { TransportOrderNumber = transportOrderNumber, CorrelationId = assigned.CorrelationId, PartyId = assigned.ShipperId, ShipmentNumber = assigned.ShipmentNumber }))
                {
                    LogAssigned(transportOrderNumber, assigned.ShipmentNumber);
                }

                return new AssignAnswer.Planned(assigned.ShipmentNumber, assigned.LoadWarning?.Text);
            // A refused assignment: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                var problem = await response.Content.ReadFromJsonAsync<Refusal>(Json, cancellationToken);
                var reason = problem?.Detail ?? $"Transport Order {transportOrderNumber} can't be assigned.";
                using (Track(Flows.TransportPlanning, TrackingKeys.Refused, new() { TransportOrderNumber = transportOrderNumber, FailureCode = problem?.Code, CorrelationId = problem?.CorrelationId }))
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
                using (Track(Flows.TransportPlanning, TrackingKeys.Succeeded, new() { TransportOrderNumber = reassigned.TransportOrderNumber, CorrelationId = reassigned.CorrelationId, PartyId = reassigned.ShipperId, ShipmentNumber = shipmentNumber }))
                {
                    LogReassigned(shipmentNumber);
                }

                return new ReassignAnswer.Reassigned(reassigned.CarriedBy, reassigned.LoadWarning?.Text);
            // A refused reassignment: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                return new ReassignAnswer.Refused(
                    await RefuseAsync(response, TmsCommand.Reassignment, $"Shipment {shipmentNumber}", "reassigned", shipmentNumber, cancellationToken: cancellationToken));
            default:
                throw Unexpected(response, "a reassignment");
        }
    }

    public Task<ReleaseAnswer> ReleaseAsync(string shipmentNumber, CancellationToken cancellationToken) =>
        SendReleaseAsync("Release Shipment", $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/release", shipmentNumber, spotPrice: null, cancellationToken);

    public Task<ReleaseAnswer> ReleaseWithSpotPriceAsync(string shipmentNumber, decimal amount, string remark, CancellationToken cancellationToken) =>
        SendReleaseAsync(
            "Release Shipment with Spot price", $"/v1/shipments/{Uri.EscapeDataString(shipmentNumber)}/release-with-spot-price", shipmentNumber,
            new SpotPriceRelease(amount, remark), cancellationToken);

    // A release, with or without a Spot price: both answer and trace the same way.
    private async Task<ReleaseAnswer> SendReleaseAsync(string spanName, string path, string shipmentNumber, SpotPriceRelease? spotPrice, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity(spanName, ActivityKind.Internal);
        using var request = await CommandAsync(path, cancellationToken);
        if (spotPrice is not null)
        {
            request.Content = JsonContent.Create(spotPrice, options: Json);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var released = await response.Content.ReadFromJsonAsync<Released>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a release without a body.");
                using (Track(Flows.TransportPlanning, TrackingKeys.Succeeded, new() { TransportOrderNumber = released.TransportOrderNumber, CorrelationId = released.CorrelationId, PartyId = released.ShipperId, ShipmentNumber = shipmentNumber }))
                {
                    LogReleased(shipmentNumber);
                }

                return new ReleaseAnswer.Released();
            // A refused release: its problem's detail is the refusal's readable message, and its code says which rule, such as a pricing failure.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity:
                var (reason, code) = await RefuseWithCodeAsync(response, TmsCommand.Release, $"Shipment {shipmentNumber}", "released", shipmentNumber, cancellationToken: cancellationToken);
                return new ReleaseAnswer.Refused(reason, code);
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
                using (Track(Flows.TransportPlanning, TrackingKeys.Succeeded, new() { CorrelationId = withdrawn.CorrelationId, ShipmentNumber = withdrawn.ShipmentNumber, CarrierBookingNumber = carrierBookingNumber }))
                {
                    LogWithdrawn(carrierBookingNumber);
                }

                return new WithdrawAnswer.Withdrawn();
            // A refused withdrawal: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict:
                return new WithdrawAnswer.Refused(
                    await RefuseAsync(response, TmsCommand.Withdrawal, $"Carrier booking {carrierBookingNumber}", "withdrawn", carrierBookingNumber: carrierBookingNumber, cancellationToken: cancellationToken));
            default:
                throw Unexpected(response, "a withdrawal");
        }
    }

    public async Task<ReleaseWithoutProofOfDeliveryAnswer> ReleaseWithoutProofOfDeliveryAsync(string transportOrderNumber, string remark, AgentRun? actingOn, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity("Release without Proof of delivery", ActivityKind.Internal);
        using var request = await CommandAsync($"/v1/transport-orders/{Uri.EscapeDataString(transportOrderNumber)}/release-without-proof-of-delivery", cancellationToken);
        request.Content = JsonContent.Create(new ProofOfDeliveryRelease(remark, CustomerServiceName.Demo, actingOn?.AgentName, actingOn?.RunId), options: Json);

        using var response = await http.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var released = await response.Content.ReadFromJsonAsync<ProofOfDeliveryReleased>(Json, cancellationToken)
                    ?? throw new HttpRequestException("The TMS API answered a release without a Proof of delivery without a body.");
                // The release starts a Message trail of its own, which TransportOrderBillable continues when the release completes the order.
                using (Track(Flows.Billing, TrackingKeys.Succeeded, new() { TransportOrderNumber = transportOrderNumber, CorrelationId = released.CorrelationId, ShipmentNumber = released.ShipmentNumber }))
                {
                    LogReleasedWithoutProofOfDelivery(transportOrderNumber, released.BillingState);
                }

                return new ReleaseWithoutProofOfDeliveryAnswer.Released(released.BillingState);
            // A refused release: its problem's detail is the refusal's readable message, and its code says which rule.
            case HttpStatusCode.NotFound or HttpStatusCode.Conflict:
                var reason = await RefuseAsync(
                    response, TmsCommand.ReleaseWithoutProofOfDelivery, $"Transport Order {transportOrderNumber}", "released without a Proof of delivery",
                    flow: Flows.Billing, transportOrderNumber: transportOrderNumber, cancellationToken: cancellationToken);
                return new ReleaseWithoutProofOfDeliveryAnswer.Refused(reason);
            default:
                throw Unexpected(response, "a release without a Proof of delivery");
        }
    }

    // What a refused reassignment, release, withdrawal or release without a Proof of delivery shares: reading the problem, tracing the refusal on the trail it
    // names, and answering with its readable reason.
    private async Task<string> RefuseAsync(
        HttpResponseMessage response, TmsCommand command, string subject, string action, string? shipmentNumber = null, string? carrierBookingNumber = null,
        string flow = Flows.TransportPlanning, string? transportOrderNumber = null, CancellationToken cancellationToken = default) =>
        (await RefuseWithCodeAsync(response, command, subject, action, shipmentNumber, carrierBookingNumber, flow, transportOrderNumber, cancellationToken)).Reason;

    private async Task<(string Reason, string? Code)> RefuseWithCodeAsync(
        HttpResponseMessage response, TmsCommand command, string subject, string action, string? shipmentNumber = null, string? carrierBookingNumber = null,
        string flow = Flows.TransportPlanning, string? transportOrderNumber = null, CancellationToken cancellationToken = default)
    {
        var problem = await response.Content.ReadFromJsonAsync<Refusal>(Json, cancellationToken);
        var reason = problem?.Detail ?? $"{subject} can't be {action}.";
        using (Track(flow, TrackingKeys.Refused, new() { TransportOrderNumber = transportOrderNumber, FailureCode = problem?.Code, CorrelationId = problem?.CorrelationId, ShipmentNumber = shipmentNumber, CarrierBookingNumber = carrierBookingNumber }))
        {
            LogRefusedSubject(command, subject, reason);
        }

        return (reason, problem?.Code);
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
    private IDisposable? Track(string flow, string outcome, TrackedKeys business)
    {
        var keys = new Dictionary<string, object?>
        {
            [TrackingKeys.Flow] = flow,
            [TrackingKeys.Outcome] = outcome,
        };
        foreach (var (key, value) in new[]
        {
            (TrackingKeys.TransportOrderNumber, business.TransportOrderNumber),
            (TrackingKeys.FailureCode, business.FailureCode),
            (TrackingKeys.CorrelationId, business.CorrelationId),
            (TrackingKeys.PartyId, business.PartyId),
            (TrackingKeys.ShipmentNumber, business.ShipmentNumber),
            (TrackingKeys.CarrierBookingNumber, business.CarrierBookingNumber),
            (TrackingKeys.TripNumber, business.TripNumber),
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} released without a Proof of delivery: {BillingState}")]
    private partial void LogReleasedWithoutProofOfDelivery(string transportOrderNumber, BillingState billingState);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier booking {CarrierBookingNumber} withdrawn")]
    private partial void LogWithdrawn(string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Command} of {Subject} refused: {Reason}")]
    private partial void LogRefusedSubject(TmsCommand command, string subject, string reason);

    // The tracking keys a command's answer names, besides the flow and the outcome; those it doesn't name stay null.
    private sealed record TrackedKeys
    {
        public string? TransportOrderNumber { get; init; }

        public string? FailureCode { get; init; }

        public string? CorrelationId { get; init; }

        public string? PartyId { get; init; }

        public string? ShipmentNumber { get; init; }

        public string? CarrierBookingNumber { get; init; }

        public string? TripNumber { get; init; }
    }

    private enum TmsCommand
    {
        ReRate,
        Assignment,
        Reassignment,
        Release,
        Withdrawal,
        ReleaseWithoutProofOfDelivery,
    }

    // What the TMS API answers a re-rate, as far as the TMS UI reads it.
    private sealed record ReRated(RatingResult RatingResult, RatingFailureView? RatingFailure, string? CorrelationId, string? ShipperId);

    // What the TMS API answers a refused command: a problem whose code says which assignment rule an assignment broke,
    // and whose correlation ID names the Message trail the TMS traced the refusal on.
    private sealed record Refusal(string? Detail, string? Code, string? CorrelationId);

    // What the TMS UI asks the TMS API to assign a Transport Order to.
    private sealed record Assignment(AssignedTo AssignedTo, string? CarrierId);

    // What the TMS API answers an assignment, as far as the TMS UI reads it.
    private sealed record Assigned(string ShipmentNumber, string? CorrelationId, string? ShipperId, LoadWarning? LoadWarning = null);

    // The load check warning the TMS API adds to an Own fleet assignment, as far as the TMS UI reads it: the sentence.
    private sealed record LoadWarning(string Text);

    // What the TMS UI asks the TMS API to reassign a Shipment to.
    private sealed record Reassignment(AssignedTo AssignedTo, string? CarrierId);

    // What the TMS API answers a reassignment, as far as the TMS UI reads it.
    private sealed record Reassigned(CarriedByView CarriedBy, string? TransportOrderNumber, string? CorrelationId, string? ShipperId, LoadWarning? LoadWarning = null);

    // What the TMS API answers a release, as far as the TMS UI reads it.
    private sealed record Released(string? TransportOrderNumber, string? CorrelationId, string? ShipperId);

    // What the TMS UI asks the TMS API to release a Shipment at.
    private sealed record SpotPriceRelease(decimal Amount, string Remark);

    // What the TMS API answers a withdrawal, as far as the TMS UI reads it.
    private sealed record Withdrawn(string? ShipmentNumber, string? CorrelationId);

    // What the TMS UI asks the TMS API to release a Transport Order without a Proof of delivery with.
    private sealed record ProofOfDeliveryRelease(string Remark, string ReleasedBy, string? AgentName, string? AgentRunId);

    // What the TMS API answers a release without a Proof of delivery, as far as the TMS UI reads it.
    private sealed record ProofOfDeliveryReleased(BillingState BillingState, string? ShipmentNumber, string? CorrelationId);
}
