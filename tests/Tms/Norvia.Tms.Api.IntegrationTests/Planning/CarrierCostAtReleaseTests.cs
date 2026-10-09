using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Planning;

/// <summary>
/// Releasing a Carrier Shipment prices its Carrier booking from the Carrier's Carrier Rate Card in the same transaction
/// that creates it, and freezes the Carrier cost on it (P9). ShipmentReleased carries the Carrier cost and
/// CarrierBookingConfirmed its total. A release the Carrier Rate Card can't price is refused with the Rating code,
/// naming the Carrier, and creates no booking. The test seed prices Proef Transport's Rotterdam to Utrecht Lane at
/// 9.50 per 100 kg up to 2,000 kg.
/// </summary>
public sealed class CarrierCostAtReleaseTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Release_CarrierShipment_PricesItsBookingFromTheCarrierRateCard_AndShipmentReleasedCarriesTheCarrierCost()
    {
        var shipmentNumber = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()), PlanningCalls.ToCarrier());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        // 910.5 kg ÷ 100 × 9.50 = 86.4975, so 86.50.
        var events = await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        var body = JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateShipmentReleased(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        var cost = body["carrierCost"]!;
        cost["currency"]!.GetValue<string>().Should().Be("EUR");
        cost["total"]!.GetValue<decimal>().Should().Be(86.50m);
        ChargeLines(cost).Should().Equal(("Freight", 86.50m));
        cost["source"]!["kind"]!.GetValue<string>().Should().Be("CarrierRateCard");
        cost["source"]!["rateCardVersion"]!["rateCardId"]!.GetValue<string>().Should().Be("CRC-TEST-0001");
        cost["source"]!["rateCardVersion"]!["version"]!.GetValue<int>().Should().Be(1);
        cost["calculatedAt"]!.GetValue<DateTimeOffset>().Should().Be(body["occurredAt"]!.GetValue<DateTimeOffset>(), "it is calculated at release");
    }

    [Theory]
    [InlineData("NoRateCard")]
    [InlineData("NoValidRateCardVersion")]
    [InlineData("NoMatchingLane")]
    [InlineData("WeightAboveHighestBreak")]
    public async Task ShipmentsToRelease_ACarrierShipmentTheCarrierRateCardCantPrice_ShowsThePricingFailure_ThatTheReleaseWouldRefuseWith(string code)
    {
        var (booking, assignment, carrierName) = ShipmentThatFailsWith(code);
        var shipmentNumber = await PlannedAsync(booking, assignment);

        var listed = (await tms.ListShipmentsToReleaseAsync(_cancellationToken)).Single(shipment => shipment!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;
        var problem = await tms.ReleaseRefusedAsync(shipmentNumber, HttpStatusCode.UnprocessableEntity, _cancellationToken);

        listed["pricingFailure"]!["code"]!.GetValue<string>().Should().Be(code).And.Be(problem["code"]!.GetValue<string>());
        listed["pricingFailure"]!["message"]!.GetValue<string>().Should().Contain(carrierName);
    }

    [Fact]
    public async Task ShipmentsToRelease_ACarrierShipmentTheCarrierRateCardPrices_ShowsNoPricingFailure()
    {
        var shipmentNumber = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()), PlanningCalls.ToCarrier());

        var listed = (await tms.ListShipmentsToReleaseAsync(_cancellationToken)).Single(shipment => shipment!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;

        listed["pricingFailure"].Should().BeNull();
    }

    [Fact]
    public async Task Release_FreightBelowTheLanesMinimum_PricesTheMinimumChargeAsAnAdjustmentLine()
    {
        var shipmentNumber = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()).Weighing(100m), PlanningCalls.ToCarrier());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        var cost = await CarrierCostOfAsync(released);
        ChargeLines(cost).Should().Equal(("Freight", 11.00m), ("MinimumChargeAdjustment", 24.00m));
        cost["total"]!.GetValue<decimal>().Should().Be(35.00m);
    }

    [Fact]
    public async Task Release_CarrierShipment_FreezesTheCarrierCostOnTheBooking_AndTheBookingsAwaitingAnAnswerShowIt()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var awaiting = (await tms.ListBookingsAwaitingAnswerAsync(_cancellationToken))
            .Single(booking => booking!["carrierBookingNumber"]!.GetValue<string>() == carrierBookingNumber)!;

        var cost = awaiting["carrierCost"]!;
        cost["total"]!.GetValue<decimal>().Should().Be(86.50m);
        ChargeLines(cost).Should().Equal(("Freight", 86.50m));
        cost["source"]!["kind"]!.GetValue<string>().Should().Be("CarrierRateCard");
        cost["source"]!["rateCardVersion"]!["version"]!.GetValue<int>().Should().Be(1);
        var onShipment = CarrierBookingCalls.BookingOf(await tms.GetShipmentAsync(shipmentNumber, _cancellationToken), carrierBookingNumber);
        JsonNode.DeepEquals(onShipment["carrierCost"], cost).Should().BeTrue("the Shipment shows the booking's frozen Carrier cost");
        awaiting.ToJsonString().Should().NotContain("freightCharge", "the Shipper's Freight Charge is never part of the Carrier booking");
    }

    [Fact]
    public async Task Release_AfterARejection_CalculatesANewCarrierCostOnTheNewBooking_AndLeavesTheRejectedOnesFrozen()
    {
        var (first, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var frozen = CarrierBookingCalls.BookingOf(await tms.GetShipmentAsync(shipmentNumber, _cancellationToken), first)["carrierCost"]!.ToJsonString();
        await tms.RejectedAsync(first, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);

        var second = (await tms.ReleasedAsync(shipmentNumber, _cancellationToken))["carrierBookingNumber"]!.GetValue<string>();

        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        CarrierBookingCalls.BookingOf(shipment, first)["carrierCost"]!.ToJsonString().Should().Be(frozen, "a Carrier cost is never recalculated");
        var newCost = CarrierBookingCalls.BookingOf(shipment, second)["carrierCost"]!;
        newCost["total"]!.GetValue<decimal>().Should().Be(86.50m);
        newCost["calculatedAt"]!.GetValue<DateTimeOffset>().Should().BeAfter(CarrierBookingCalls.BookingOf(shipment, first)["carrierCost"]!["calculatedAt"]!.GetValue<DateTimeOffset>());
    }

    [Fact]
    public async Task Confirm_CarrierBooking_PublishesCarrierBookingConfirmedWithTheTotalOfTheCarrierCost()
    {
        var (carrierBookingNumber, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);

        var confirmed = await tms.ConfirmedAsync(carrierBookingNumber, PlanningCalls.CarrierId, CarrierBookingCalls.NewReference(), _cancellationToken);

        var events = await tms.PublishedAsync(confirmed["correlationId"]!.GetValue<string>(), "CarrierBookingConfirmed", _cancellationToken);
        var body = JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateCarrierBookingConfirmed(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["carrierCostTotal"]!.GetValue<decimal>().Should().Be(86.50m);
    }

    [Fact]
    public async Task Release_OwnFleetShipment_HasNoCarrierCost()
    {
        var shipmentNumber = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()), PlanningCalls.ToOwnFleet());

        var released = await tms.ReleasedAsync(shipmentNumber, _cancellationToken);

        var events = await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        var body = JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!;
        body["carrierCost"].Should().BeNull();
    }

    [Theory]
    [InlineData("NoRateCard")]
    [InlineData("NoValidRateCardVersion")]
    [InlineData("NoMatchingLane")]
    [InlineData("WeightAboveHighestBreak")]
    public async Task Release_CarrierRateCardThatCantPriceTheShipment_IsRefusedWithTheRatingCode_NamingTheCarrier_AndCreatesNoBooking(string code)
    {
        var (booking, assignment, carrierName) = ShipmentThatFailsWith(code);
        var shipmentNumber = await PlannedAsync(booking, assignment);

        var problem = await tms.ReleaseRefusedAsync(shipmentNumber, HttpStatusCode.UnprocessableEntity, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be(code);
        problem["detail"]!.GetValue<string>().Should().Contain(carrierName);
        problem["correlationId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        var shipment = await tms.GetShipmentAsync(shipmentNumber, _cancellationToken);
        shipment["status"]!.GetValue<string>().Should().Be("Planned", "a refused release changes nothing");
        shipment["carrierBookings"]!.AsArray().Should().BeEmpty("a release that can't be priced creates no booking");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.ShipmentsReleased(shipmentNumber).Should().BeEmpty("a refused release publishes nothing");
    }

    [Fact]
    public async Task Release_ShipmentsOneAfterTheOther_SucceedOrFailEachOnItsOwn()
    {
        var priced = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()), PlanningCalls.ToCarrier());
        var (unpricedBooking, unpricedAssignment, _) = ShipmentThatFailsWith("NoMatchingLane");
        var unpriced = await PlannedAsync(unpricedBooking, unpricedAssignment);
        var alsoPriced = await PlannedAsync(Bookings.Valid(TmsCalls.NewReference()), PlanningCalls.ToCarrier());

        await tms.ReleaseRefusedAsync(unpriced, HttpStatusCode.UnprocessableEntity, _cancellationToken);
        var first = await tms.ReleasedAsync(priced, _cancellationToken);
        var last = await tms.ReleasedAsync(alsoPriced, _cancellationToken);

        first["carrierBookingNumber"]!.GetValue<string>().Should().MatchRegex("^CB-[0-9]{7}$");
        last["carrierBookingNumber"]!.GetValue<string>().Should().MatchRegex("^CB-[0-9]{7}$");
        (await tms.GetShipmentAsync(unpriced, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
    }

    private static (JsonObject Booking, JsonObject Assignment, string CarrierName) ShipmentThatFailsWith(string code)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        var assignment = PlanningCalls.ToCarrier();
        switch (code)
        {
            case "NoRateCard":
                return (booking, PlanningCalls.ToCarrier(PlanningCalls.OtherCarrierId), "Reserve Transport");
            case "NoValidRateCardVersion":
                return (booking.CollectedFrom(new DateTimeOffset(2050, 3, 1, 8, 0, 0, TimeSpan.Zero)), assignment, "Proef Transport");
            case "NoMatchingLane":
                return (booking.DeliveredTo("9711 AA"), assignment, "Proef Transport");
            case "WeightAboveHighestBreak":
                return (booking.Weighing(6_000m), assignment, "Proef Transport");
            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, "Not a rating failure code.");
        }
    }

    /// <summary>
    /// A Transport Order the Planner assigned, whose ShipmentPlanned is out. Rating has settled first, so its trigger doesn't
    /// change the order while it is assigned: the Shipper's Rate Card prices the order, or fails for it as the Carrier Rate Card does.
    /// </summary>
    private async Task<string> PlannedAsync(JsonObject booking, JsonObject assignment)
    {
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingToSettleAsync(number, _cancellationToken);
        var assigned = await tms.AssignPlannedAsync(number, assignment, _cancellationToken);
        await tms.PublishedAsync(assigned["correlationId"]!.GetValue<string>(), "ShipmentPlanned", _cancellationToken);
        return assigned["shipmentNumber"]!.GetValue<string>();
    }

    private async Task<JsonNode> CarrierCostOfAsync(JsonNode released)
    {
        var events = await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "ShipmentReleased", _cancellationToken);
        return JsonNode.Parse(events.Should().ContainSingle().Subject.Body.ToString())!["carrierCost"]!;
    }

    private static IEnumerable<(string Type, decimal Amount)> ChargeLines(JsonNode cost) =>
        cost["chargeLines"]!.AsArray().Select(line => (line!["type"]!.GetValue<string>(), line["amount"]!.GetValue<decimal>()));
}
