using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Application.Failures;

namespace Norvia.HealthCheck.Application.UnitTests.Failures;

public sealed class ListFailuresHandlerTests
{
    private readonly IEstateConfiguration _estate = Substitute.For<IEstateConfiguration>();

    private void Estate(EstateConfiguration facts) => _estate.ReadAsync(Arg.Any<CancellationToken>()).Returns(facts);

    [Fact]
    public async Task ListFailedRuns_ListsOnlyTheFlowsThatFailed_TheMostFailedFirst()
    {
        Estate(new("as-found", []) { Flows = [new("billing", true, FailedRuns: 0), new("carrier-booking", true, FailedRuns: 2), new("carrier-tracking", false, FailedRuns: 14), new("unread", false, FailedRuns: null)] });

        var result = await new ListFailedRunsHandler(_estate).Handle(new ListFailedRunsQuery(), TestContext.Current.CancellationToken);

        result.Value.Estate.Should().Be("as-found");
        result.Value.WindowDays.Should().Be(EstateConfiguration.FailureWindowDays);
        result.Value.Flows.Should().Equal(new FailedFlow("carrier-tracking", 14, false), new FailedFlow("carrier-booking", 2, true));
    }

    [Fact]
    public async Task ListDeadLetterQueues_ListsOnlyTheEntitiesThatHoldMessages_TheFullestFirst()
    {
        Estate(new("as-found", [new("billing", true, 0), new("carrier-booking", true, 1), new("carrier-tracking", false, 3), new("unread", false, null)]));

        var result = await new ListDeadLetterQueuesHandler(_estate).Handle(new ListDeadLetterQueuesQuery(), TestContext.Current.CancellationToken);

        result.Value.Queues.Should().Equal(new DeadLetteredEntity("carrier-tracking", 3, false), new DeadLetteredEntity("carrier-booking", 1, true));
    }

    [Fact]
    public async Task TheFixedEstate_HasNothingToList()
    {
        Estate(new("fixed", [new("billing", true, 0)]) { Flows = [new("billing", true)] });

        (await new ListFailedRunsHandler(_estate).Handle(new ListFailedRunsQuery(), TestContext.Current.CancellationToken)).Value.Flows.Should().BeEmpty();
        (await new ListDeadLetterQueuesHandler(_estate).Handle(new ListDeadLetterQueuesQuery(), TestContext.Current.CancellationToken)).Value.Queues.Should().BeEmpty();
    }
}
