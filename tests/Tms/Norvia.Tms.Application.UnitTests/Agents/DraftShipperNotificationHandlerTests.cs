using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class DraftShipperNotificationHandlerTests
{
    private readonly IReadRepository<ExceptionCase> _repository = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();

    public DraftShipperNotificationHandlerTests()
    {
        _carriers.ListActive().Returns([new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true)]);
    }

    private DraftShipperNotificationHandler Handler => new(_repository, _agent, _carriers);

    [Fact]
    public async Task Handle_AnOpenException_AsksTheNotificationAgent_ForItsExceptionAndOrder_OnTheExceptionsOwnTrail()
    {
        var exception = Exceptions.OpenExceptions.DeliveryFailed();
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(exception);
        var suggestion = new Suggestion("A draft.", [new Evidence("e", "r1", "get_shipper_view")], "Edit it.", Confidence.Medium);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await Handler.Handle(new DraftShipperNotificationQuery("EX-0000005"), CancellationToken.None);

        result.Value.Should().Be(suggestion);
        await _agent.Received(1).AskAsync(
            new AgentRequest(
                "planner-notification",
                PlannerQuestions.DraftShipperNotification("EX-0000005", exception.TransportOrderNumber.Value),
                exception.CorrelationId.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AResolvedException_IsNotFound_AndAsksNobody()
    {
        var exception = Exceptions.OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.ClaimHandledOutsideTms, "Claim filed", new CorrelationId("corr-resolve"), Samples.Now.AddDays(2));
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(exception);

        var result = await Handler.Handle(new DraftShipperNotificationQuery("EX-0000005"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("EX-0000005", true)]
    [InlineData("not-a-number", false)]
    public async Task Handle_AnUnknownException_IsNotFound_AndAsksNobody(string number, bool missing)
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(missing ? null : Exceptions.OpenExceptions.DeliveryFailed());

        var result = await Handler.Handle(new DraftShipperNotificationQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Exceptions.OpenExceptions.DeliveryFailed());
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var result = await Handler.Handle(new DraftShipperNotificationQuery("EX-0000005"), CancellationToken.None);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Theory]
    [InlineData("Hello, Van Dijk Transport could not deliver.")]
    [InlineData("Hello, van dijk transport could not deliver.")]
    [InlineData("Hello, about EX-0000005: delivery failed.")]
    [InlineData("Hello, we will refund €50.")]
    [InlineData("Hello, a credit of 50 euros follows.")]
    [InlineData("Hello, the fee is USD 20.")]
    public async Task Handle_ADraftThatNamesACarrierTheExceptionOrAPrice_NeverReachesThePlanner(string draft)
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Exceptions.OpenExceptions.DeliveryFailed());
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(new Suggestion(draft, [], "Edit it.", Confidence.Medium));

        var result = await Handler.Handle(new DraftShipperNotificationQuery("EX-0000005"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.DraftNotAllowed);
    }

    [Fact]
    public async Task Handle_ACleanDraft_PassesThrough()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Exceptions.OpenExceptions.DeliveryFailed());
        var suggestion = new Suggestion("Hello, your order is in transit. We have no update on a new delivery time yet.", [], "Edit it.", Confidence.Medium);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await Handler.Handle(new DraftShipperNotificationQuery("EX-0000005"), CancellationToken.None);

        result.Value.Should().Be(suggestion);
    }
}
