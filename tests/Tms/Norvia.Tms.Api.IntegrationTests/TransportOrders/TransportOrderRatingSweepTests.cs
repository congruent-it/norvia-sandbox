namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// On a TMS whose acceptances never trigger Rating, the rating worker's timer sweep still rates every Transport Order
/// left Not yet rated, as after a crash between acceptance and Rating.
/// </summary>
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
    public async Task Sweep_MoreOrdersItCantRateThanOneBatch_StillRatesTheNewerOnes()
    {
        // SHP-0003 has no Rate Card, so its orders stay Not yet rated, ahead of the newer one in every sweep.
        for (var i = 0; i < 3; i++)
        {
            var unpriced = Bookings.Valid(TmsCalls.NewReference());
            unpriced["shipperId"] = "SHP-0003";
            await tms.SubmitAcceptedAsync(unpriced, TmsCalls.NewCorrelationId(), _cancellationToken);
        }

        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);

        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
    }
}
