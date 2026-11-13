using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Refusals;
using Norvia.IntegrationConsole.Application.Refusals.List;

namespace Norvia.IntegrationConsole.Application.UnitTests.Refusals;

public sealed class ListRefusedCallsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly IRefusedCalls _refusals = Substitute.For<IRefusedCalls>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_Always_AsksForTheLast24Hours()
    {
        Record();

        var list = await ListAsync();

        await _refusals.Received(1).RefusalsAsync(Now.AddHours(-24), Arg.Any<CancellationToken>());
        list.From.Should().Be(Now.AddHours(-24));
        list.To.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_ForSeveralRefusals_ListsThemNewestFirst_AndLeavesOutOlderThan24Hours()
    {
        Record(
            Refused(Now.AddHours(-3)),
            Refused(Now.AddMinutes(-1)),
            Refused(Now.AddHours(-24).AddSeconds(-1)),
            Refused(Now.AddHours(-1)));

        var list = await ListAsync();

        list.Calls.Select(call => call.Time).Should().Equal(Now.AddMinutes(-1), Now.AddHours(-1), Now.AddHours(-3));
    }

    [Fact]
    public async Task Handle_ForAStatusAndAnOperation_NarrowsTheCalls_ButStillOffersEveryOperation()
    {
        Record(
            Refused(Now.AddHours(-3), status: 403, operation: "Submit Transport Order"),
            Refused(Now.AddHours(-2), status: 429, operation: "Find Transport Order"),
            Refused(Now.AddHours(-1), status: 429, operation: "Submit Transport Order"));

        var list = await ListAsync(status: 429, operation: " Submit Transport Order ");

        list.Calls.Should().ContainSingle().Which.Time.Should().Be(Now.AddHours(-1));
        list.Operations.Should().Equal("Find Transport Order", "Submit Transport Order");
    }

    [Fact]
    public async Task Handle_ForSeveralCallers_GroupsTheShownCallsByCallerAndStatus_MostRefusedFirst()
    {
        Record(
            Refused(Now.AddHours(-5), status: 403, caller: "van-dijk-transport"),
            Refused(Now.AddHours(-4), status: 403, caller: "van-dijk-transport"),
            Refused(Now.AddHours(-3), status: 429, caller: "van-dijk-transport"),
            Refused(Now.AddHours(-2), status: 401, caller: null),
            Refused(Now.AddHours(-1), status: 403, caller: "van-dijk-transport"));

        var list = await ListAsync();

        list.Groups.Should().Equal(
            new RefusalGroup("van-dijk-transport", 403, 3),
            new RefusalGroup("unknown", 401, 1),
            new RefusalGroup("van-dijk-transport", 429, 1));
    }

    private static RecordedRefusal Refused(DateTimeOffset time, int status = 403, string operation = "Submit Transport Order", string? caller = "van-dijk-transport")
        => new(time, operation, status, "NotAllowedToSubmit", caller, "corr-" + time.Ticks);

    private void Record(params RecordedRefusal[] refusals)
        => _refusals.RefusalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(refusals);

    private async Task<RefusedCallList> ListAsync(int? status = null, string? operation = null)
        => await new ListRefusedCallsHandler(_refusals, _time).Handle(new ListRefusedCallsQuery(status, operation), CancellationToken.None);
}
