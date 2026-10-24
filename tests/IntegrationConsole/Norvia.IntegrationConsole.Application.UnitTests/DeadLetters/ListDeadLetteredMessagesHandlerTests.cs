using Ardalis.Result;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.UnitTests.DeadLetters;

public sealed class ListDeadLetteredMessagesHandlerTests
{
    private static readonly DateTimeOffset Enqueued = new(2026, 9, 26, 8, 30, 0, TimeSpan.Zero);

    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();

    [Fact]
    public async Task Handle_ForAQueueWithMessages_ListsThemOldestFirst()
    {
        Peeks(Message(7, Enqueued.AddMinutes(5)), Message(3, Enqueued), Message(5, Enqueued));

        var result = await HandleAsync("carrier-tracking");

        result.Value.Entity.Should().Be("carrier-tracking");
        result.Value.Messages.Select(message => message.SequenceNumber).Should().Equal(3, 5, 7);
        result.Value.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Always_CarriesTheTrackingContractsKeysAndNoBody()
    {
        var keys = new[] { new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042") };
        Peeks(new RecordedDeadLetter(3, Enqueued, "MaxDeliveryCountExceeded", "Failed 10 times", 10, "corr-1", keys));

        var result = await HandleAsync("carrier-tracking");

        result.Value.Messages.Should().ContainSingle().Which.Should().Be(new RecordedDeadLetter(3, Enqueued, "MaxDeliveryCountExceeded", "Failed 10 times", 10, "corr-1", keys));
        typeof(RecordedDeadLetter).GetProperties().Select(property => property.Name).Should().NotContain("Body", because: "a peek never holds a body; the application properties are for the agent view, which the page does not render");
    }

    [Fact]
    public async Task Handle_ForMoreThanAPage_ListsThePage_AndSaysThereIsMore()
    {
        Peeks([.. Enumerable.Range(1, ListDeadLetteredMessagesHandler.PageSize + 1).Select(number => Message(number, Enqueued.AddSeconds(number)))]);

        var result = await HandleAsync("carrier-tracking");

        result.Value.Messages.Should().HaveCount(ListDeadLetteredMessagesHandler.PageSize);
        result.Value.Messages[^1].SequenceNumber.Should().Be(ListDeadLetteredMessagesHandler.PageSize);
        result.Value.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Always_OnlyPeeks()
    {
        Peeks(Message(1, Enqueued));

        await HandleAsync(" carrier-tracking ");

        await _queues.Received(1).PeekAsync("carrier-tracking", ListDeadLetteredMessagesHandler.PageSize + 1, Arg.Any<CancellationToken>());
        await _queues.DidNotReceive().OpenAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _queues.DidNotReceive().DeadLetterQueuesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAnEmptyQueue_ListsNothing()
    {
        Peeks();

        var result = await HandleAsync("carrier-tracking");

        result.Value.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForAQueueThatDoesNotExist_IsNotFound()
    {
        _queues.PeekAsync("nope", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RecordedDeadLetter>?)null);

        var result = await HandleAsync("nope");

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Handle_WithoutAQueueName_IsInvalid_AndPeeksNothing(string entity)
    {
        var result = await HandleAsync(entity);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _queues.DidNotReceive().PeekAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private async Task<Result<DeadLetteredMessageList>> HandleAsync(string entity) =>
        await new ListDeadLetteredMessagesHandler(_queues).Handle(new ListDeadLetteredMessagesQuery(entity), TestContext.Current.CancellationToken);

    private void Peeks(params RecordedDeadLetter[] messages) =>
        _queues.PeekAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(messages);

    private static RecordedDeadLetter Message(long sequenceNumber, DateTimeOffset enqueued) =>
        new(sequenceNumber, enqueued, "MaxDeliveryCountExceeded", null, 10, null, []);
}
