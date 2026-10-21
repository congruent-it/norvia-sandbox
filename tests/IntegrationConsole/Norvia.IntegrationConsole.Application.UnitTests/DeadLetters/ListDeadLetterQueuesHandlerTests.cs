using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.List;

namespace Norvia.IntegrationConsole.Application.UnitTests.DeadLetters;

public sealed class ListDeadLetterQueuesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_ForQueuesAndSubscriptions_ListsOneRowForEach_WithCountAndAgeFromTheClock()
    {
        Record(
            new RecordedDeadLetters("carrier-tracking", 3, Now.AddMinutes(-90)),
            new RecordedDeadLetters("shipment-events/carrier-booking", 1, Now.AddDays(-2)));

        var list = await ListAsync();

        list.At.Should().Be(Now);
        list.Queues.Should().BeEquivalentTo(
        [
            new DeadLetterEntity("shipment-events/carrier-booking", 1, TimeSpan.FromDays(2)),
            new DeadLetterEntity("carrier-tracking", 3, TimeSpan.FromMinutes(90)),
        ]);
    }

    [Fact]
    public async Task Handle_ForQueuesWithAndWithoutMessages_ListsThoseWithMessagesFirst_TheOldestFirst_ThenTheRestByName()
    {
        Record(
            new RecordedDeadLetters("trip-events/trip-dispatch", 0, null),
            new RecordedDeadLetters("carrier-proof-of-delivery", 2, Now.AddMinutes(-5)),
            new RecordedDeadLetters("carrier-tracking", 0, null),
            new RecordedDeadLetters("invoicing-events/shipper-invoice", 7, Now.AddHours(-3)));

        var list = await ListAsync();

        list.Queues.Select(queue => queue.Name).Should().Equal(
            "invoicing-events/shipper-invoice", "carrier-proof-of-delivery", "carrier-tracking", "trip-events/trip-dispatch");
    }

    [Fact]
    public async Task Handle_ForAQueueWithNoMessages_ShowsNoAge()
    {
        Record(new RecordedDeadLetters("carrier-tracking", 0, Now.AddHours(-1)));

        var list = await ListAsync();

        list.Queues.Should().ContainSingle().Which.OldestAge.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForAMessageEnqueuedAfterTheClock_ShowsNoNegativeAge()
    {
        Record(new RecordedDeadLetters("carrier-tracking", 1, Now.AddSeconds(2)));

        var list = await ListAsync();

        list.Queues.Single().OldestAge.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Handle_ForAnEstateWithoutQueues_ListsNone()
    {
        Record();

        var list = await ListAsync();

        list.Queues.Should().BeEmpty();
    }

    private void Record(params RecordedDeadLetters[] recorded) =>
        _queues.DeadLetterQueuesAsync(Arg.Any<CancellationToken>()).Returns(recorded);

    private async Task<DeadLetterQueueList> ListAsync() =>
        await new ListDeadLetterQueuesHandler(_queues, _time).Handle(new ListDeadLetterQueuesQuery(), TestContext.Current.CancellationToken);
}
