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
}
