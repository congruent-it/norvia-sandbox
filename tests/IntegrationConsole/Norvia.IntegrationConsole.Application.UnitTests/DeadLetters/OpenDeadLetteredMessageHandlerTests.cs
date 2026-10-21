using Ardalis.Result;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;

namespace Norvia.IntegrationConsole.Application.UnitTests.DeadLetters;

public sealed class OpenDeadLetteredMessageHandlerTests
{
    private static readonly RecordedDeadLetter Listed =
        new(3, new DateTimeOffset(2026, 9, 26, 8, 30, 0, TimeSpan.Zero), "MaxDeliveryCountExceeded", null, 10, "corr-1", []);

    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();

    [Fact]
    public async Task Handle_ForAMessageOnTheQueue_ReturnsItsBodyAndApplicationProperties()
    {
        var opened = new RecordedDeadLetterBody(Listed, """{"shipmentNumber":"SH-0042"}""", new Dictionary<string, string> { ["shipmentNumber"] = "SH-0042" });
        _queues.OpenAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(opened);

        var result = await HandleAsync("carrier-tracking", 3);

        result.Value.Should().Be(opened);
    }

    [Fact]
    public async Task Handle_Always_OnlyPeeksTheOneMessage()
    {
        await HandleAsync(" carrier-tracking ", 3);

        await _queues.Received(1).OpenAsync("carrier-tracking", 3, Arg.Any<CancellationToken>());
        await _queues.DidNotReceive().PeekAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAMessageThatIsGone_IsNotFound()
    {
        var result = await HandleAsync("carrier-tracking", 3);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("carrier-tracking", -1)]
    public async Task Handle_WithoutAQueueOrAValidSequenceNumber_IsInvalid_AndPeeksNothing(string entity, long sequenceNumber)
    {
        var result = await HandleAsync(entity, sequenceNumber);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _queues.DidNotReceive().OpenAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private async Task<Result<RecordedDeadLetterBody>> HandleAsync(string entity, long sequenceNumber) =>
        await new OpenDeadLetteredMessageHandler(_queues).Handle(new OpenDeadLetteredMessageQuery(entity, sequenceNumber), TestContext.Current.CancellationToken);
}
