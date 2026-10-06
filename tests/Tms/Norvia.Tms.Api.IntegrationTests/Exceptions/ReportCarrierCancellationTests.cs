using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.CarrierBookings.CarrierBookingCalls;
using static Norvia.Tms.Api.IntegrationTests.Exceptions.ExceptionCalls;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>
/// The Carrier cancellation Logic App reports a Cancelled or Withdrawn Carrier booking's message as told or not delivered (P7.8). Not
/// delivered flags Carrier not told with the technical cause and publishes CarrierCancellationNotDelivered; a repeat report is
/// ignored, logged and publishes nothing.
/// </summary>
public sealed class ReportCarrierCancellationTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task NotDelivered_ACancelledBooking_FlagsCarrierNotTold_AndPublishesOneCarrierCancellationNotDelivered()
    {
        var (shipmentNumber, carrierBookingNumber, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var correlationId = Guid.NewGuid().ToString();

        using var response = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken, 503, "Service unavailable.", correlationId);

        var answer = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, answer);
        JsonNode.Parse(answer)!["ignored"]!.GetValue<bool>().Should().BeFalse();
        var message = (await tms.PublishedAsync(correlationId, "CarrierCancellationNotDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateCarrierCancellationNotDelivered(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["kind"]!.GetValue<string>().Should().Be("Cancelled");
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Service unavailable.");
    }

    [Fact]
    public async Task NotDelivered_AWithdrawnBooking_PublishesItWithKindWithdrawn()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        var correlationId = Guid.NewGuid().ToString();

        using var response = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken, correlationId: correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = (await tms.PublishedAsync(correlationId, "CarrierCancellationNotDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        CanonicalModelSchemas.ValidateCarrierCancellationNotDelivered(body).IsValid.Should().BeTrue();
        body["kind"]!.GetValue<string>().Should().Be("Withdrawn");
    }

    [Fact]
    public async Task NotDelivered_ShowsCarrierNotToldInShipmentsToRelease_WithTheCause()
    {
        var (shipmentNumber, carrierBookingNumber, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken, 503, "Service unavailable.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);
        var outcome = toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!["lastCarrierBookingOutcome"]!;
        outcome["status"]!.GetValue<string>().Should().Be("Cancelled");
        outcome["carrierNotTold"]!.GetValue<bool>().Should().BeTrue();
        outcome["carrierNotToldMessage"]!.GetValue<string>().Should().Be("Service unavailable.");
    }

    [Fact]
    public async Task Told_ACancelledBooking_PublishesNothing_AndDoesNotFlagCarrierNotTold()
    {
        var (shipmentNumber, carrierBookingNumber, _, exceptionNumber) = await tms.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var cancelled = await tms.CancelCarrierBookingAsync(exceptionNumber, Cancellation(), _cancellationToken);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber));

        using var response = await tms.ReportCancellationToldAsync(carrierBookingNumber, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber)).Should().Be(before);
        var toRelease = await tms.ListShipmentsToReleaseAsync(_cancellationToken);
        toRelease.Single(s => s!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!["lastCarrierBookingOutcome"]!["carrierNotTold"]!
            .GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Reports_ForABookingAlreadyTold_AreIgnored_AndPublishNothing()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        using var told = await tms.ReportCancellationToldAsync(carrierBookingNumber, _cancellationToken);
        told.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber));

        using var repeatTold = await tms.ReportCancellationToldAsync(carrierBookingNumber, _cancellationToken);
        using var notDelivered = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken);

        foreach (var response in new[] { repeatTold, notDelivered })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["ignored"]!.GetValue<bool>().Should().BeTrue();
        }

        tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber)).Should().Be(before);
    }

    [Fact]
    public async Task NotDelivered_ARepeatReport_IsIgnored_AndPublishesNothingAgain()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        using var first = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber));

        using var repeat = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken);

        JsonNode.Parse(await repeat.Content.ReadAsStringAsync(_cancellationToken))!["ignored"]!.GetValue<bool>().Should().BeTrue();
        tms.Published.Count(m => NamesCarrierBooking(m, carrierBookingNumber)).Should().Be(before);
    }

    [Fact]
    public async Task Reports_ABookingNorviaDidntEnd_AreRefused_WithBookingNotCancelled()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        using var told = await tms.ReportCancellationToldAsync(carrierBookingNumber, _cancellationToken);
        using var notDelivered = await tms.ReportCancellationNotDeliveredAsync(carrierBookingNumber, _cancellationToken);

        told.StatusCode.Should().Be(HttpStatusCode.Conflict);
        notDelivered.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notDelivered.Content.ReadAsStringAsync(_cancellationToken)).Should().Contain("BookingNotCancelled");
    }

    [Fact]
    public async Task Reports_AnUnknownBookingNumber_AreRefused_WithBookingNotFound()
    {
        using var told = await tms.ReportCancellationToldAsync("CB-9999999", _cancellationToken);
        using var notDelivered = await tms.ReportCancellationNotDeliveredAsync("CB-9999999", _cancellationToken);

        told.StatusCode.Should().Be(HttpStatusCode.NotFound);
        notDelivered.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reports_WithoutTheReportRole_AreRefused()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        using var noToken = tms.CreateTmsClient(token: null);
        using var planner = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ManageRole]));

        foreach (var (caller, expected) in new[] { (noToken, HttpStatusCode.Unauthorized), (planner, HttpStatusCode.Forbidden) })
        {
            foreach (var route in new[] { "report-cancellation-told", "report-cancellation-not-delivered" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/{route}", UriKind.Relative))
                {
                    Content = JsonContent.Create(new JsonObject { ["statusCode"] = 503, ["message"] = "Down" }),
                };
                request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
                using var response = await caller.SendAsync(request, _cancellationToken);
                response.StatusCode.Should().Be(expected, route);
            }
        }
    }

    [Fact]
    public async Task NotDelivered_WithoutACauseOrCorrelationId_IsAClientError()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.WithdrawnAsync(carrierBookingNumber, _cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/report-cancellation-not-delivered", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject()),
        };

        using var response = await tms.CarrierReport.SendAsync(request, _cancellationToken);
        using var toldWithoutTrail = await tms.CarrierReport.PostAsync(
            new Uri($"/v1/carrier-bookings/{carrierBookingNumber}/report-cancellation-told", UriKind.Relative), content: null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        toldWithoutTrail.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
