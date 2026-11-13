using Norvia.HealthCheck.Application.Cost;

namespace Norvia.HealthCheck.Application.UnitTests.Cost;

public sealed class GetCostViewHandlerTests
{
    private static readonly DateOnly Yesterday = new(2026, 11, 12);
    private readonly ICostReader _reader = Substitute.For<ICostReader>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();

    public GetCostViewHandlerTests() => _time.GetUtcNow().Returns(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private void Reads(params CostDay[] days) => _reader.ReadAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(days);

    private static CostDay Day(int daysAgo, int orders, decimal workspace, decimal sql = 1m, bool provisional = false) =>
        new(Yesterday.AddDays(1 - daysAgo), orders, provisional, [new("log-workspace", workspace), new("sql", sql)]);

    private Task<Ardalis.Result.Result<CostView>> HandleAsync() =>
        new GetCostViewHandler(_reader, _time).Handle(new GetCostViewQuery(), TestContext.Current.CancellationToken).AsTask();

    [Fact]
    public async Task Handle_ReadsTwoWeeksOfWholeDaysEndingYesterday()
    {
        Reads();

        await HandleAsync();

        await _reader.Received().ReadAsync(new DateOnly(2026, 10, 30), Yesterday, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SpendUpWithFlatVolume_ShowsTheResourceThatRoseAndNoChangeInVolume()
    {
        Reads(Day(10, 40, 1m), Day(9, 40, 1m), Day(2, 40, 3m), Day(1, 40, 3m));

        var view = (await HandleAsync()).Value;

        view.Resources[0].Name.Should().Be("log-workspace");
        view.Resources[0].Relative.Should().Be(2m);
        view.Resources[1].Relative.Should().Be(0m);
        view.Volume.Relative.Should().Be(0m);
        view.Total.Previous.Should().Be(2m);
        view.Total.Current.Should().Be(4m);
    }

    [Fact]
    public async Task Handle_ProvisionalDays_AreShownButLeftOutOfTheAverages()
    {
        Reads(Day(10, 40, 1m), Day(2, 40, 1m), Day(1, 40, 9m, provisional: true));

        var view = (await HandleAsync()).Value;

        view.Days.Should().HaveCount(3);
        view.HasProvisionalDays.Should().BeTrue();
        view.Resources.Single(resource => resource.Name == "log-workspace").Current.Should().Be(1m);
    }

    [Fact]
    public async Task Handle_NoDaysInThePreviousPeriod_HasNoRelativeChange()
    {
        Reads(Day(2, 40, 1m));

        var view = (await HandleAsync()).Value;

        view.Total.Previous.Should().BeNull();
        view.Total.Relative.Should().BeNull();
        view.HasProvisionalDays.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ASpendOfZeroBefore_HasNoRelativeChange()
    {
        Reads(Day(10, 40, 0m), Day(1, 40, 2m));

        (await HandleAsync()).Value.Resources.Single(resource => resource.Name == "log-workspace").Relative.Should().BeNull();
    }
}
