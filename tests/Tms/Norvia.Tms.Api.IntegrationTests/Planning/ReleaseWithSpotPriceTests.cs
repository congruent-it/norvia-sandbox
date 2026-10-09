using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>
/// After the Carrier Rate Card failed to price a Carrier Shipment, the Planner releases it on its own at a Spot price (P9).
/// The Carrier cost frozen on the new booking has one Freight line, the source Spot and the remark. A Spot price is refused
/// when the Carrier Rate Card can price the Shipment, and needs an amount and a remark. The test seed has no Lane from
/// Rotterdam to Groningen in Proef Transport's Carrier Rate Card.
/// </summary>
public sealed class ReleaseWithSpotPriceTests(TmsFixture tms)
{
    private const string Remark = "Agreed with Proef Transport by phone, 480 euro";

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReleaseWithSpotPrice_AfterAPricingFailure_ReleasesWithTheSpotCarrierCostAndItsRemark()
    {
        var shipmentNumber = await UnpricedAsync();
        await tms.ReleaseRefusedAsync(shipmentNumber, HttpStatusCode.UnprocessableEntity, _cancellationToken);

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var released = JsonNode.Parse(body)!;
        released["carrierBookingNumber"]!.GetValue<string>().Should().MatchRegex("^CB-[0-9]{7}$");
        var events = await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        var published = JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentReleased(published);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        var cost = published["carrierCost"]!;
        cost["currency"]!.GetValue<string>().Should().Be("EUR");
        cost["total"]!.GetValue<decimal>().Should().Be(480m);
        cost["chargeLines"]!.AsArray().Should().ContainSingle().Which!["type"]!.GetValue<string>().Should().Be("Freight");
        cost["source"]!["kind"]!.GetValue<string>().Should().Be("Spot");
        cost["source"]!["remark"]!.GetValue<string>().Should().Be(Remark);
        cost["source"]!["rateCardVersion"].Should().BeNull();
    }

    [Fact]
    public async Task ReleaseWithSpotPrice_FreezesTheSpotPriceOnTheBooking_AndTheBookingsAwaitingAnAnswerShowSourceSpot()
    {
        var shipmentNumber = await UnpricedAsync();

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken);
        var carrierBookingNumber = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["carrierBookingNumber"]!.GetValue<string>();

        var awaiting = (await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken))
            .Single(booking => booking!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!;
        var cost = awaiting["carrierCost"]!;
        cost["total"]!.GetValue<decimal>().Should().Be(480m);
        cost["source"]!["kind"]!.GetValue<string>().Should().Be("Spot");
        cost["source"]!["remark"]!.GetValue<string>().Should().Be(Remark);
        var onShipment = CarrierBookingCalls.BookingOf(await tms.GetShipmentAsync(shipmentNumber, _cancellationToken), carrierBookingNumber);
        JsonNode.DeepEquals(onShipment["carrierCost"], cost).Should().BeTrue("the Shipment shows the booking's frozen Spot price");
    }

    [Fact]
    public async Task ReleaseWithSpotPrice_WhenTheCarrierRateCardCanPriceTheShipment_IsRefused_AndReleasesNothing()
    {
        var shipmentNumber = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()));

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        JsonNode.Parse(body)!["code"]!.GetValue<string>().Should().Be("CarrierRateCardPricesShipment");
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned");
        shipment["carrierBookings"]!.AsArray().Should().BeEmpty();
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.ShipmentsReleased(shipmentNumber).Should().BeEmpty("a refused release publishes nothing");
    }

    [Fact]
    public async Task ReleaseWithSpotPrice_ForOwnFleet_IsRefused()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingToSettleAsync(number, _cancellationToken);
        var shipmentNumber = (await tms.AssignPlannedAsync(number, PlanningCalls.ToOwnFleet(), _cancellationToken))["shipmentNumber"]!.GetValue<string>();

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["code"]!.GetValue<string>().Should().Be("SpotPriceNotAllowed");
    }

    [Theory]
    [InlineData(null, "Remark", "Amount")]
    [InlineData(0.0, "Remark", "Amount")]
    [InlineData(-1.0, "Remark", "Amount")]
    [InlineData(10.005, "Remark", "Amount")]
    [InlineData(480.0, null, "Remark")]
    [InlineData(480.0, "  ", "Remark")]
    public async Task ReleaseWithSpotPrice_WithoutAValidAmountAndRemark_IsRefused_AndReleasesNothing(double? amount, string? remark, string invalid)
    {
        var shipmentNumber = await UnpricedAsync();

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot((decimal?)amount, remark), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        JsonNode.Parse(body)!["errors"]!.AsObject().Should().ContainKey(invalid);
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
    }

    [Fact]
    public async Task ReleaseWithSpotPrice_SentAgainUnderTheSameKeyWithAnotherPrice_AnswersAsBefore_AndKeepsTheFirstSpotPrice()
    {
        var shipmentNumber = await UnpricedAsync();
        var key = Guid.NewGuid().ToString();
        using var first = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken, idempotencyKey: key);
        var released = JsonNode.Parse(await first.Content.ReadAsStringAsync(_cancellationToken))!;

        using var again = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(520m, "Changed my mind"), _cancellationToken, idempotencyKey: key);

        var body = await again.Content.ReadAsStringAsync(_cancellationToken);
        again.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var repeat = JsonNode.Parse(body)!;
        repeat["repeat"]!.GetValue<bool>().Should().BeTrue();
        repeat["carrierBookingNumber"]!.GetValue<string>().Should().Be(released["carrierBookingNumber"]!.GetValue<string>());
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["carrierBookings"]!.AsArray().Should().ContainSingle();
        CarrierBookingCalls.BookingOf(shipment, released["carrierBookingNumber"]!.GetValue<string>())["carrierCost"]!["total"]!
            .GetValue<decimal>().Should().Be(480m);
    }

    [Fact]
    public async Task ReleaseWithSpotPrice_WithARemarkOverTheLimit_IsRefused()
    {
        var shipmentNumber = await UnpricedAsync();

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, new string('x', 501)), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(TmsFixture.IntakeRole)]
    [InlineData(TmsFixture.AnswerRole)]
    [InlineData(TmsFixture.ReportRole)]
    public async Task ReleaseWithSpotPrice_TokenWithoutThePlannerRole_IsForbidden(string role)
    {
        var shipmentNumber = await UnpricedAsync();
        using var caller = tms.CreateTmsClient(tms.IssueToken([role]));

        using var response = await tms.ReleaseWithSpotPriceAsync(shipmentNumber, PlanningCalls.Spot(480m, Remark), _cancellationToken, caller);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
    }

    /// <summary>A Planned Shipment whose Carrier's Carrier Rate Card has no Lane to its destination.</summary>
    private Task<string> UnpricedAsync() => PlannedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredTo("9711 AA"));

    private async Task<string> PlannedAsync(JsonObject booking)
    {
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingToSettleAsync(number, _cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, PlanningCalls.ToCarrier(), _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        return assigned["shipmentNumber"]!.GetValue<string>();
    }
}
