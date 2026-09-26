using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Customer service finds the rating exceptions and re-rates one through the TMS API (P2). On this TMS the test seed is
/// never corrected, so an order to Düsseldorf, where SHP-0001's Rate Card has no Lane, fails again when it is re-rated.
/// Re-rating after the Rate Card is corrected is in <see cref="TransportOrderReRateAfterCorrectionTests"/>.
/// </summary>
public sealed class TransportOrderReRateTests(TmsFixture tms)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListRatingExceptions_ListsTheRatingFailedOrdersWithTheirFailure_AndNoOthers()
    {
        var failed = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        var rated = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(failed, "RatingFailed", _cancellationToken);
        await tms.WaitForRatingResultAsync(rated, "Rated", _cancellationToken);

        var exceptions = await tms.ListRatingExceptionsAsync(_cancellationToken);

        var exception = exceptions.Should().ContainSingle(e => e!["transportOrderNumber"]!.GetValue<string>() == failed).Subject!;
        exception["shipper"]!["id"]!.GetValue<string>().Should().Be("SHP-0001");
        exception["ratingFailure"]!["code"]!.GetValue<string>().Should().Be("NoMatchingLane");
        exception["ratingFailure"]!["message"]!.GetValue<string>().Should().Be(
            "Rate Card RC-TEST-0001 version 1 has no Lane from Rotterdam to Düsseldorf.");
        exceptions.Should().NotContain(e => e!["transportOrderNumber"]!.GetValue<string>() == rated);

        // Other tests share this TMS, so each order listed is checked to be Rating failed rather than counted.
        foreach (var listed in exceptions)
        {
            var order = await tms.GetTransportOrderAsync(listed!["transportOrderNumber"]!.GetValue<string>(), _cancellationToken);
            order["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
        }
    }

    [Fact]
    public async Task ReRate_FailsAgain_StaysRatingFailed_AndPublishesAnotherTransportOrderRatingFailedOnTheReRatesTrail()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), intake, _cancellationToken);
        await tms.PublishedAsync(intake, "TransportOrderRatingFailed", _cancellationToken);

        var reRate = TmsCalls.NewCorrelationId();
        using var response = await tms.ReRateAsync(number, reRate, _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var result = JsonNode.Parse(body)!;
        result["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
        result["ratingFailure"]!["code"]!.GetValue<string>().Should().Be("NoMatchingLane");

        var message = (await tms.PublishedAsync(reRate, "TransportOrderRatingFailed", _cancellationToken)).Should().ContainSingle().Subject;
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = reRate,
            ["transportOrderNumber"] = number,
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["failureCode"] = "NoMatchingLane",
        });
        var published = JsonNode.Parse(message.Body.ToString())!;
        CanonicalModelSchemas.ValidateTransportOrderRatingFailed(published).IsValid.Should().BeTrue();
        tms.PublishedAbout(number, "TransportOrderRatingFailed").Should().HaveCount(2, "the first rating and the re-rate each publish their outcome");
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
    }

    [Fact]
    public async Task ReRate_RatedOrder_IsRefused_ChangesNothing_AndPublishesNothing()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), intake, _cancellationToken);
        await tms.PublishedAsync(intake, "TransportOrderRated", _cancellationToken);
        var before = await tms.GetTransportOrderAsync(number, _cancellationToken);

        var reRate = TmsCalls.NewCorrelationId();
        using var response = await tms.ReRateAsync(number, reRate, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;
        problem["detail"]!.GetValue<string>().Should().Be(
            $"Transport Order {number} is Rated; only a Transport Order whose Rating failed can be re-rated.");
        var after = await tms.GetTransportOrderAsync(number, _cancellationToken);
        JsonNode.DeepEquals(before, after).Should().BeTrue("a frozen Freight Charge stays frozen");

        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.Published.Should().NotContain(m => m.CorrelationId == reRate);
        tms.PublishedAbout(number, "TransportOrderRated").Should().ContainSingle();
    }

    [Fact]
    public async Task ReRate_NotRatedSeparately_IsRefused()
    {
        using var wms = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], clientId: TmsFixture.WmsClientId));
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken, wms);

        using var response = await tms.ReRateAsync(number, TmsCalls.NewCorrelationId(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["ratingResult"]!.GetValue<string>().Should().Be("NotRatedSeparately");
    }

    [Fact]
    public async Task ReRate_UnknownTransportOrder_IsNotFound()
    {
        using var response = await tms.ReRateAsync("TO-9999999", TmsCalls.NewCorrelationId(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReRate_Always_TracesUnderTheReRatesTrailWithTheTrackingContractKeys()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);

        var reRate = TmsCalls.NewCorrelationId();
        using var response = await tms.ReRateAsync(number, reRate, _cancellationToken);

        var failedAgain = await tms.TmsLogAsync(log => log["correlationId"] == reRate && log["failureCode"] is not null, _cancellationToken);
        failedAgain["flow"].Should().Be("transport-order-rating");
        failedAgain["partyId"].Should().Be("SHP-0001");
        failedAgain["transportOrderNumber"].Should().Be(number);
        failedAgain["failureCode"].Should().Be("NoMatchingLane");
    }

    [Fact]
    public async Task ReRate_WithoutACorrelationId_StartsATrailOfItsOwn()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);

        using var response = await tms.Tms.PostAsync(new Uri($"/v1/transport-orders/{number}/re-rate", UriKind.Relative), content: null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Should().ContainSingle().Subject;
        (await tms.PublishedAsync(correlationId, "TransportOrderRatingFailed", _cancellationToken)).Should().ContainSingle();
    }
}
