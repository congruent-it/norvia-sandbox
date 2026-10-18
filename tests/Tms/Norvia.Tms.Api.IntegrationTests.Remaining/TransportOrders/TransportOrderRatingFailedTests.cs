using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Rating failed (P2) at the TMS's boundary: an order its Shipper's Rate Card can't price stays Accepted, shows the
/// failure on the query API and publishes one TransportOrderRatingFailed. The test seed gives SHP-0003 no Rate Card, no
/// version of SHP-0001's is valid from 2050, it has no Lane into Düsseldorf, and its Lane to Utrecht Science Park (3584)
/// prices up to 5,000 kg.
/// </summary>
public sealed class TransportOrderRatingFailedTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    public static TheoryData<string> FailureCodes() => ["NoRateCard", "NoValidRateCardVersion", "NoMatchingLane", "WeightAboveHighestBreak"];

    [Theory]
    [MemberData(nameof(FailureCodes))]
    public async Task Submit_OrderTheRateCardCantPrice_IsRatingFailedWithTheCode_AndStaysAccepted(string code)
    {
        var number = await tms.SubmitAcceptedAsync(BookingThatFailsWith(code), TmsCalls.NewCorrelationId(), _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);

        order["status"]!.GetValue<string>().Should().Be("Accepted", "a Shipper's pricing gap never becomes a rejection of their booking");
        order["ratingFailure"]!["code"]!.GetValue<string>().Should().Be(code);
        order["ratingFailure"]!["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        order["freightCharge"].Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(FailureCodes))]
    public async Task Submit_OrderTheRateCardCantPrice_PublishesExactlyOneTransportOrderRatingFailed(string code)
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(BookingThatFailsWith(code), correlationId, _cancellationToken);

        var events = await tms.PublishedAsync(correlationId, "TransportOrderRatingFailed", _cancellationToken);

        var message = events.Should().ContainSingle().Subject;
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["transportOrderNumber"] = number,
            ["flow"] = "transport-order-rating",
            ["partyId"] = code == "NoRateCard" ? "SHP-0003" : "SHP-0001",
            ["eventType"] = "TransportOrderRatingFailed",
            ["failureCode"] = code,
        });

        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateTransportOrderRatingFailed(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId, "rating continues the Message trail of the intake");
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        body["ratingFailure"]!["code"]!.GetValue<string>().Should().Be(code);
        tms.Published.Should().NotContain(m => m.CorrelationId == correlationId && m.Subject == "TransportOrderRated");
    }

    [Fact]
    public async Task Submit_OrderTheRateCardCantPrice_ShowsWhatItPublishes()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), correlationId, _cancellationToken);

        var published = JsonNode.Parse((await tms.PublishedAsync(correlationId, "TransportOrderRatingFailed", _cancellationToken)).Single().Body.ToString())!;
        var stored = await tms.GetTransportOrderAsync(number, _cancellationToken);

        stored["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
        JsonNode.DeepEquals(stored["ratingFailure"], published["ratingFailure"]).Should().BeTrue(
            $"the query shows {stored["ratingFailure"]!.ToJsonString()} but the event carries {published["ratingFailure"]!.ToJsonString()}");
        stored["ratingFailure"]!["message"]!.GetValue<string>().Should().Be(
            "Rate Card RC-TEST-0001 version 1 has no Lane from Rotterdam to Düsseldorf.");
    }

    [Fact]
    public async Task Submit_OrderTheRateCardCantPrice_RatingTracesCarryTheFailureCode_WithTheOutcomeSucceeded()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), correlationId, _cancellationToken);

        var failed = await tms.TmsLogAsync(
            log => log["correlationId"] == correlationId && log["flow"] == "transport-order-rating" && log["failureCode"] is not null,
            _cancellationToken);

        failed["failureCode"].Should().Be("NoMatchingLane");
        failed["transportOrderNumber"].Should().Be(number);
        failed["outcome"].Should().Be("Succeeded", "Rating failed is Rating's answer, not a failed flow: the order stays Accepted");
    }

    private static JsonObject BookingThatFailsWith(string code)
    {
        var booking = Bookings.Valid(TmsCalls.NewReference());
        switch (code)
        {
            case "NoRateCard":
                booking["shipperId"] = "SHP-0003";
                return booking;
            case "NoValidRateCardVersion":
                return booking.CollectedFrom(new DateTimeOffset(2050, 3, 1, 8, 0, 0, TimeSpan.Zero));
            case "NoMatchingLane":
                return booking.DeliveredToDusseldorf();
            case "WeightAboveHighestBreak":
                return booking.DeliveredTo("3584 CS").Weighing(6_000m);
            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, "Not a rating failure code.");
        }
    }
}
