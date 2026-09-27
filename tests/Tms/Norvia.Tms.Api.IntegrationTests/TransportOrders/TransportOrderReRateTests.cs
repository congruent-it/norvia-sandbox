using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Customer service finds the rating exceptions and re-rates one through the TMS API (P2). On this TMS the test seed is
/// never corrected, so an order to Düsseldorf, where SHP-0001's Rate Card has no Lane, fails again when it is re-rated.
/// Re-rating after the Rate Card is corrected is in <see cref="TransportOrderReRateAfterCorrectionTests"/>. A re-rate
/// continues the Message trail of the order's intake, so its events are found by the intake's correlation ID.
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
    public async Task ReRate_FailsAgain_StaysRatingFailed_AndPublishesAnotherTransportOrderRatingFailed()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), intake, _cancellationToken);
        await tms.PublishedAsync(intake, "TransportOrderRatingFailed", _cancellationToken);

        using var response = await tms.ReRateAsync(number, _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var result = JsonNode.Parse(body)!;
        result["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
        result["ratingFailure"]!["code"]!.GetValue<string>().Should().Be("NoMatchingLane");

        var events = await tms.PublishedAsync(intake, "TransportOrderRatingFailed", count: 2, _cancellationToken);
        events.Should().HaveCount(2, "the first rating and the re-rate each publish their outcome");
        var reRated = events[^1];
        reRated.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = intake,
            ["transportOrderNumber"] = number,
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["failureCode"] = "NoMatchingLane",
        });
        CanonicalModelSchemas.ValidateTransportOrderRatingFailed(JsonNode.Parse(reRated.Body.ToString())!).IsValid.Should().BeTrue();
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
    }

    [Fact]
    public async Task ReRate_RatedOrder_IsRefused_ChangesNothing_AndPublishesNothing()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), intake, _cancellationToken);
        await tms.PublishedAsync(intake, "TransportOrderRated", _cancellationToken);
        var publishedBefore = tms.Published.Count(m => m.CorrelationId == intake);
        var before = await tms.GetTransportOrderAsync(number, _cancellationToken);

        using var response = await tms.ReRateAsync(number, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;
        problem["detail"]!.GetValue<string>().Should().Be(
            $"Transport Order {number} is Rated; only a Transport Order whose Rating failed can be re-rated.");
        var after = await tms.GetTransportOrderAsync(number, _cancellationToken);
        JsonNode.DeepEquals(before, after).Should().BeTrue("a frozen Freight Charge stays frozen");

        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.Published.Count(m => m.CorrelationId == intake).Should().Be(publishedBefore);
    }

    [Fact]
    public async Task ReRate_NotRatedSeparately_IsRefused()
    {
        using var wms = tms.CreateTmsClient(tms.IssueToken([TmsFixture.IntakeRole], clientId: TmsFixture.WmsClientId));
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken, wms);

        using var response = await tms.ReRateAsync(number, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["ratingResult"]!.GetValue<string>().Should().Be("NotRatedSeparately");
    }

    [Fact]
    public async Task ReRate_SentAgainUnderItsKey_AnswersAsBefore_AndPublishesNothingMore()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), intake, _cancellationToken);
        await tms.PublishedAsync(intake, "TransportOrderRatingFailed", _cancellationToken);
        var key = Guid.NewGuid().ToString();
        using var first = await tms.ReRateAsync(number, _cancellationToken, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await tms.PublishedAsync(intake, "TransportOrderRatingFailed", count: 2, _cancellationToken);

        using var retry = await tms.ReRateAsync(number, _cancellationToken, key);

        var body = await retry.Content.ReadAsStringAsync(_cancellationToken);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var result = JsonNode.Parse(body)!;
        result["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
        result["repeat"]!.GetValue<bool>().Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(3), _cancellationToken); // Let the outbox run, so an event would show up.
        tms.Published.Count(m => m.CorrelationId == intake && m.Subject == "TransportOrderRatingFailed").Should().Be(
            2, "a retry rates nothing, so it publishes nothing");
    }

    [Fact]
    public async Task ReRate_WithoutAnIdempotencyKey_IsABadRequest()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);

        using var response = await tms.TmsUi.PostAsync(new Uri($"/v1/transport-orders/{number}/re-rate", UriKind.Relative), content: null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;
        problem["errors"]!["Idempotency-Key"].Should().NotBeNull();
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["ratingResult"]!.GetValue<string>().Should().Be("RatingFailed");
    }

    [Fact]
    public async Task ReRate_UnknownTransportOrder_IsNotFound()
    {
        using var response = await tms.ReRateAsync("TO-9999999", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReRate_Always_TracesOnTheIntakesTrailWithTheTrackingContractKeys()
    {
        var intake = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), intake, _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);

        using var response = await tms.ReRateAsync(number, _cancellationToken);

        var failedAgain = await tms.TmsLogAsync(
            log => log["correlationId"] == intake && log.Text.Contains("re-rated", StringComparison.Ordinal),
            _cancellationToken);
        failedAgain["flow"].Should().Be("transport-order-rating");
        failedAgain["partyId"].Should().Be("SHP-0001");
        failedAgain["transportOrderNumber"].Should().Be(number);
        failedAgain["failureCode"].Should().Be("NoMatchingLane");
        failedAgain["outcome"].Should().Be("Succeeded");
    }
}
