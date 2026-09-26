using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Search;

namespace Norvia.IntegrationConsole.Application.UnitTests.Trails;

public sealed class SearchTrailsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly ITrailSource _source = Substitute.For<ITrailSource>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_WithoutATimeRange_SearchesTheLast24Hours()
    {
        var result = await SearchAsync(new SearchTrailsQuery("TO-2026-000123"));

        await _source.Received(1).SearchAsync(new TrailSearch("TO-2026-000123", Now.AddHours(-24), Now), Arg.Any<CancellationToken>());
        result.Value.From.Should().Be(Now.AddHours(-24));
        result.Value.To.Should().Be(Now);
    }

    [Theory]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")] // a correlation ID
    [InlineData("TO-2026-000123")] // a Transport Order number
    [InlineData("SH-2026-000456")] // a Shipment number
    [InlineData("VDT-BK-88213")] // a Carrier booking number
    [InlineData("TR-2026-000789")] // a Trip number
    [InlineData("evt-5c1d")] // a source message ID
    public async Task Handle_WithAnyKindOfKey_PassesTheKeyToTheSource(string key)
    {
        await SearchAsync(new SearchTrailsQuery(key));

        await _source.Received(1).SearchAsync(Arg.Is<TrailSearch>(search => search.Key == key), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithAKeyPastedWithSpaces_SearchesTheKeyWithout()
    {
        await SearchAsync(new SearchTrailsQuery("  TO-2026-000123 \n"));

        await _source.Received(1).SearchAsync(Arg.Is<TrailSearch>(search => search.Key == "TO-2026-000123"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithoutAKey_IsInvalid_AndSearchesNothing(string key)
    {
        var result = await SearchAsync(new SearchTrailsQuery(key));

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Invalid);
        await _source.DidNotReceiveWithAnyArgs().SearchAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_WithARangeOfNoTime_IsInvalid_AndSearchesNothing()
    {
        var result = await SearchAsync(new SearchTrailsQuery("TO-2026-000123", TimeSpan.Zero));

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Invalid);
        await _source.DidNotReceiveWithAnyArgs().SearchAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_WithARangeWithinTheRetentionPeriod_SearchesThatRange()
    {
        var result = await SearchAsync(new SearchTrailsQuery("TO-2026-000123", TimeSpan.FromDays(7)));

        await _source.Received(1).SearchAsync(new TrailSearch("TO-2026-000123", Now.AddDays(-7), Now), Arg.Any<CancellationToken>());
        result.Value.From.Should().Be(Now.AddDays(-7));
    }

    [Fact]
    public async Task Handle_WithARangeLongerThanTheRetentionPeriod_SearchesOnlyTheRetentionPeriod()
    {
        // Nothing older is kept, so looking further back only makes the search slower.
        var result = await SearchAsync(new SearchTrailsQuery("TO-2026-000123", TimeSpan.FromDays(365)), retentionDays: 90);

        await _source.Received(1).SearchAsync(new TrailSearch("TO-2026-000123", Now.AddDays(-90), Now), Arg.Any<CancellationToken>());
        result.Value.From.Should().Be(Now.AddDays(-90));
    }

    [Fact]
    public async Task Handle_WithTrailsThatFailedAndTrailsThatDidNot_ListsTheFailedOnesFirst_EachGroupLatestFirst()
    {
        _source.SearchAsync(Arg.Any<TrailSearch>(), Arg.Any<CancellationToken>()).Returns(
        [
            Trail("booked-an-hour-ago", lastSeenHoursAgo: 1, Outcome.Succeeded),
            Trail("failed-three-hours-ago", lastSeenHoursAgo: 3, Outcome.Succeeded, Outcome.Failed),
            Trail("refused-just-now", lastSeenHoursAgo: 0, Outcome.Refused),
            Trail("dead-lettered-two-hours-ago", lastSeenHoursAgo: 2, Outcome.Retrying, Outcome.DeadLettered),
        ]);

        var result = await SearchAsync(new SearchTrailsQuery("TO-2026-000123"));

        result.Value.Trails.Select(trail => trail.CorrelationId).Should().Equal(
            "dead-lettered-two-hours-ago",
            "failed-three-hours-ago",
            "refused-just-now",
            "booked-an-hour-ago");
    }

    [Fact]
    public async Task Handle_ForATrailWithSeveralOutcomes_ShowsItsWorst()
    {
        _source.SearchAsync(Arg.Any<TrailSearch>(), Arg.Any<CancellationToken>()).Returns(
        [
            Trail("retried-then-dead-lettered", lastSeenHoursAgo: 1, Outcome.Succeeded, Outcome.DeadLettered, Outcome.Retrying),
            Trail("refused-at-the-edge", lastSeenHoursAgo: 2, Outcome.Refused, Outcome.Succeeded),
            Trail("no-outcome-recorded", lastSeenHoursAgo: 3),
        ]);

        var result = await SearchAsync(new SearchTrailsQuery("TO-2026-000123"));

        result.Value.Trails.Select(trail => trail.WorstOutcome).Should().Equal(Outcome.DeadLettered, Outcome.Refused, null);
    }

    [Fact]
    public async Task Handle_ForAFoundTrail_ShowsItsFlowsTimesAndBusinessKeys()
    {
        var keys = new BusinessKey[] { new(BusinessKeyKind.TransportOrderNumber, "TO-2026-000123") };
        _source.SearchAsync(Arg.Any<TrailSearch>(), Arg.Any<CancellationToken>()).Returns(
        [
            new FoundTrail("corr-1", ["transport-order-intake"], Now.AddMinutes(-5), Now.AddMinutes(-4), [Outcome.Succeeded], keys),
        ]);

        var result = await SearchAsync(new SearchTrailsQuery("corr-1"));

        result.Value.Trails.Should().ContainSingle().Which.Should().BeEquivalentTo(new TrailSummary(
            "corr-1", ["transport-order-intake"], Now.AddMinutes(-5), Now.AddMinutes(-4), Outcome.Succeeded, keys));
    }

    private static FoundTrail Trail(string correlationId, int lastSeenHoursAgo, params Outcome[] outcomes) =>
        new(correlationId, ["transport-order-intake"], Now.AddHours(-lastSeenHoursAgo - 1), Now.AddHours(-lastSeenHoursAgo), outcomes, []);

    private async Task<Ardalis.Result.Result<TrailSearchResult>> SearchAsync(SearchTrailsQuery query, int retentionDays = 90)
    {
        var handler = new SearchTrailsHandler(_source, _time, Options.Create(new IntegrationRetention { IntegrationRetentionDays = retentionDays }));
        return await handler.Handle(query, TestContext.Current.CancellationToken);
    }
}
