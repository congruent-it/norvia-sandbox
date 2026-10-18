namespace Norvia.Tms.Api.IntegrationTests.Planner;

/// <summary>
/// The Planner's home page counts a Carrier booking awaiting an answer only once it is overdue: it was requested longer ago than the
/// threshold (two hours), or its collection window has opened. The reads here see a clock the test moves; the TMS API's stays put.
/// </summary>
public sealed class PlannerHomeOverdueBookingsTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ABookingUnansweredForLongerThanTheThreshold_IsCounted_AndOneInsideItIsNot()
    {
        await using var reader = await TmsQueryReader.OpenAsync(tms, _cancellationToken, () => _now);
        var before = await reader.PlannerHomeAsync(_cancellationToken);
        await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var requestedAt = DateTimeOffset.UtcNow;

        _now = requestedAt.AddHours(1);
        var inside = await reader.PlannerHomeAsync(_cancellationToken);
        _now = requestedAt.AddHours(3);
        var beyond = await reader.PlannerHomeAsync(_cancellationToken);

        inside.OverdueBookings.Should().Be(before.OverdueBookings);
        beyond.OverdueBookings.Should().Be(before.OverdueBookings + 1);
    }
}
