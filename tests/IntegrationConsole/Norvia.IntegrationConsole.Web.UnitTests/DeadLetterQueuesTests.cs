using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class DeadLetterQueuesTests : ConsoleWebContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();
    private readonly FakeTimeProvider _time = new(Now);

    public DeadLetterQueuesTests()
    {
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<TimeProvider>(_time);
        Answer(Entity("shipment-events/carrier-booking", 2, TimeSpan.FromHours(3.5)), Entity("carrier-tracking", 0, null));
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        typeof(DeadLetterQueues).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_ForQueuesAndSubscriptions_ShowsOneRowForEach_WithCountAndAgeOfTheOldest()
    {
        var page = Render<DeadLetterQueues>();

        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("shipment-events/carrier-booking").And.Contain("2").And.Contain("3 h 30 min");
        rows[1].TextContent.Should().Contain("carrier-tracking").And.Contain("0").And.Contain("–");
    }

    [Fact]
    public void Render_WithoutQueues_SaysSo()
    {
        Answer();

        var page = Render<DeadLetterQueues>();

        page.Find("[role=status]").TextContent.Should().Contain("no queues or subscriptions");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        _deadLetters.ListAsync(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<DeadLetterQueueList>().Task);

        var page = Render<DeadLetterQueues>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the dead-letter queues");
    }

    [Fact]
    public void Render_WhenTheQueuesCantBeListed_SaysSo_WithoutTheReason()
    {
        _deadLetters.ListAsync(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>())
            .Returns<DeadLetterQueueList>(_ => throw new HttpRequestException("403 AuthorizationFailed"));

        var page = Render<DeadLetterQueues>();

        page.Find("[role=alert]").TextContent.Should().Contain("The dead-letter queues couldn't be listed.");
        page.Markup.Should().NotContain("AuthorizationFailed");
    }

    [Fact]
    public void Render_WhileOpen_RefreshesItself_AndShowsACountThatChangesMeanwhile()
    {
        var page = Render<DeadLetterQueues>();
        page.FindAll("tbody tr")[1].TextContent.Should().Contain("carrier-tracking");
        Answer(Entity("carrier-tracking", 5, TimeSpan.FromMinutes(1)), Entity("shipment-events/carrier-booking", 2, TimeSpan.FromHours(3.5)));

        _time.Advance(TimeSpan.FromSeconds(5));

        page.WaitForAssertion(() => page.FindAll("tbody tr")[0].TextContent.Should().Contain("carrier-tracking").And.Contain("5").And.Contain("1 min"));
        _deadLetters.Received(2).ListAsync(new ListDeadLetterQueuesQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_WhenARefreshFails_SaysSo_AndRecoversOnTheNext()
    {
        var page = Render<DeadLetterQueues>();
        _deadLetters.ListAsync(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>())
            .Returns<DeadLetterQueueList>(_ => throw new HttpRequestException("503"));

        _time.Advance(TimeSpan.FromSeconds(5));
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("couldn't be listed"));
        Answer(Entity("carrier-tracking", 1, TimeSpan.FromMinutes(2)));
        _time.Advance(TimeSpan.FromSeconds(5));

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle());
    }

    [Fact]
    public async Task Dispose_WhenThePageIsClosed_StopsRefreshing()
    {
        var page = Render<DeadLetterQueues>();

        await DisposeComponentsAsync();
        _time.Advance(TimeSpan.FromSeconds(30));

        page.Should().NotBeNull();
        _ = _deadLetters.Received(1).ListAsync(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "–")]
    [InlineData(20, "under a minute")]
    [InlineData(7 * 60, "7 min")]
    [InlineData(2 * 3600 + 5 * 60, "2 h 5 min")]
    [InlineData(3 * 86400 + 4 * 3600, "3 d 4 h")]
    public void Age_Always_ReadsInTheLargestTwoUnits(int? seconds, string expected)
    {
        DeadLetterText.Age(seconds is { } value ? TimeSpan.FromSeconds(value) : null).Should().Be(expected);
    }

    private static DeadLetterEntity Entity(string name, long count, TimeSpan? age) => new(name, count, age);

    private void Answer(params DeadLetterEntity[] queues) =>
        _deadLetters.ListAsync(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>()).Returns(new DeadLetterQueueList(Now, queues));
}
