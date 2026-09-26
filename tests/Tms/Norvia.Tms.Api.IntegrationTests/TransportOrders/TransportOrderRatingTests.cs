using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Rating (P2) as an outside observer sees it: a Transport Order submitted through intake, then its rating result on the
/// query API and its events on the Transport Order events topic. The Rate Card is the test seed's: Rotterdam to Utrecht,
/// which prices <see cref="Bookings.Valid"/> (910.5 kg) at 12.40 per 100 kg.
/// </summary>
public sealed class TransportOrderRatingTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_OrderTheRateCardCovers_IsRatedWithTheExpectedFreightCharge()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        order["status"]!.GetValue<string>().Should().Be("Accepted", "the rating result is separate from the status");
        ShouldBeTheFixturesFreightCharge(order["freightCharge"]!);
    }

    [Fact]
    public async Task Submit_OrderTheRateCardCovers_PublishesExactlyOneTransportOrderRated()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), correlationId, _cancellationToken);

        var events = await tms.PublishedAsync(correlationId, "TransportOrderRated", _cancellationToken);

        var message = events.Should().ContainSingle().Subject;
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["transportOrderNumber"] = number,
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["eventType"] = "TransportOrderRated",
        });

        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateTransportOrderRated(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId, "rating continues the Message trail of the intake");
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        body["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
        ShouldBeTheFixturesFreightCharge(body["freightCharge"]!);
    }

    [Fact]
    public async Task Submit_OrderTheRateCardCovers_StoresWhatItPublishes()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), correlationId, _cancellationToken);

        var published = JsonNode.Parse((await tms.PublishedAsync(correlationId, "TransportOrderRated", _cancellationToken)).Single().Body.ToString())!;
        var stored = await tms.GetTransportOrderAsync(number, _cancellationToken);

        stored["ratingResult"]!.GetValue<string>().Should().Be("Rated");
        JsonNode.DeepEquals(stored["freightCharge"], published["freightCharge"]).Should().BeTrue(
            $"the query shows {stored["freightCharge"]!.ToJsonString()} but the event carries {published["freightCharge"]!.ToJsonString()}");
    }

    [Fact]
    public async Task Submit_ShipperWithoutARateCard_IsAcceptedAndShownNotYetRated()
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        booking["shipperId"] = "SHP-0003";
        var number = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.GetTransportOrderAsync(number, _cancellationToken);

        order["ratingResult"]!.GetValue<string>().Should().Be("NotYetRated");
        order["freightCharge"].Should().BeNull();
    }

    [Fact]
    public async Task Submit_ThroughTheWmsChannel_IsNotRatedSeparately_AndPublishesNoRatingEvent()
    {
        using var wms = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], clientId: TmsFixture.WmsClientId));
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), correlationId, _cancellationToken, wms);

        await tms.PublishedAsync(correlationId, "TransportOrderAccepted", _cancellationToken);
        var order = await tms.GetTransportOrderAsync(number, _cancellationToken);

        order["channel"]!.GetValue<string>().Should().Be("Wms");
        order["ratingResult"]!.GetValue<string>().Should().Be("NotRatedSeparately");
        order["freightCharge"].Should().BeNull();
        tms.Published.Should().NotContain(m => m.CorrelationId == correlationId && m.Subject == "TransportOrderRated");
    }

    [Fact]
    public async Task Submit_OrderTheRateCardCovers_RatingTracesCarryTheTrackingContractKeys()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), correlationId, _cancellationToken);

        var rated = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["flow"] == "transport-order-rating", _cancellationToken);

        rated["transportOrderNumber"].Should().Be(number);
        rated["partyId"].Should().Be("SHP-0001");
    }

    internal static void ShouldBeTheFixturesFreightCharge(JsonNode freightCharge)
    {
        freightCharge["currency"]!.GetValue<string>().Should().Be("EUR");
        freightCharge["total"]!.GetValue<decimal>().Should().Be(112.90m, "910.5 kg ÷ 100 × 12.40 = 112.902, rounded to cents");
        var line = freightCharge["chargeLines"]!.AsArray().Should().ContainSingle().Subject!;
        line["type"]!.GetValue<string>().Should().Be("Freight");
        line["amount"]!.GetValue<decimal>().Should().Be(112.90m);
        freightCharge["rateCardVersion"]!["rateCardId"]!.GetValue<string>().Should().Be("RC-TEST-0001");
        freightCharge["rateCardVersion"]!["version"]!.GetValue<int>().Should().Be(1);
    }
}
