namespace Norvia.Tms.Api.IntegrationTests.Planner;

/// <summary>With nothing to do the Planner's home page counts 0 everywhere and the read still succeeds. A TMS of its own, so nothing else has made work.</summary>
public sealed class PlannerHomeNothingToDoTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Counts_WithNothingToDo_AreZero_AndTheReadSucceeds()
    {
        await using var reader = await TmsQueryReader.OpenAsync(tms, _cancellationToken);

        var counts = await reader.PlannerHomeAsync(_cancellationToken);

        counts.WaitingForPlanning.Should().Be(0);
        counts.ShipmentsToRelease.Should().Be(0);
        counts.OverdueBookings.Should().Be(0);
        counts.OpenExceptions.Should().Be(0);
        counts.DeliveredWithoutProofOfDelivery.Should().Be(0);
        counts.ShipmentsToPutOnTrips.Should().Be(0);
    }
}
