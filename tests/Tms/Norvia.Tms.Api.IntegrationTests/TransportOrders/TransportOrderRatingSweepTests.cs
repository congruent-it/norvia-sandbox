namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// On a TMS whose acceptances never trigger Rating, the rating worker's timer sweep still rates every Transport Order
/// left Not yet rated, as after a crash between acceptance and Rating.
/// </summary>
[Trait("AppHost", "Own")]
public sealed class TransportOrderRatingSweepTests(SweepOnlyTmsFixture tms) : IClassFixture<SweepOnlyTmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_TriggerMissed_TheSweepRatesTheOrder()
    {
        var correlationId = TmsCalls.NewCorrelationId();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), correlationId, _cancellationToken);

        var order = await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);

        TransportOrderRatingTests.ShouldBeTheFixturesFreightCharge(order["freightCharge"]!);
        (await tms.PublishedAsync(correlationId, "TransportOrderRated", _cancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task Sweep_MoreOrdersItCantPriceThanOneBatch_FailsThemAndStillRatesTheNewerOnes()
    {
        // SHP-0003 has no Rate Card, so its orders, ahead of the newer one, become Rating failed.
        var unpricedNumbers = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var unpriced = Bookings.Valid(TmsCalls.NewReference());
            unpriced["shipperId"] = "SHP-0003";
            unpricedNumbers.Add(await tms.SubmitAcceptedAsync(unpriced, TmsCalls.NewCorrelationId(), _cancellationToken));
        }

        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);

        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        foreach (var unpricedNumber in unpricedNumbers)
        {
            await tms.WaitForRatingResultAsync(unpricedNumber, "RatingFailed", _cancellationToken);
        }
    }
}
