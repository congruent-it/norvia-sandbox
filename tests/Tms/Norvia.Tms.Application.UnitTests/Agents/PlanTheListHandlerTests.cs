using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

/// <summary>Plan the waiting list (#445): the orders in the list's order, one question each, the running tally, the cap and the partial list.</summary>
#pragma warning disable CA2012 // NSubstitute's Returns takes the call's ValueTask as the call being configured.
public sealed class PlanTheListHandlerTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly List<SuggestAssignmentQuery> _asked = [];

    [Fact]
    public async Task Handle_AsksAboutEachOrderInTheListsOrder_WithTheOrdersAlreadySuggestedForOwnFleetAsTheTally()
    {
        Waiting("TO-0000001", "TO-0000002", "TO-0000003", "TO-0000004");
        Answer("TO-0000001", SuggestedActionKind.AssignOwnFleet);
        Answer("TO-0000002", SuggestedActionKind.AssignCarrier, "CAR-0001");
        Answer("TO-0000003", SuggestedActionKind.AssignOwnFleet);
        Answer("TO-0000004", SuggestedActionKind.NeedsAPerson);

        var plan = (await Handler(10).Handle(new PlanTheListQuery(), CancellationToken.None)).Value;

        plan.Suggestions.Select(listed => listed.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002", "TO-0000003", "TO-0000004");
        plan.Stopped.Should().BeNull();
        _asked.Select(query => query.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002", "TO-0000003", "TO-0000004");
        _asked.Select(query => query.AlreadyAssumed).Should().SatisfyRespectively(
            first => first.Should().BeEmpty("nothing is assumed before the first order"),
            second => second.Should().Equal("TO-0000001"),
            third => third.Should().Equal("TO-0000001"),
            fourth => fourth.Should().Equal("TO-0000001", "TO-0000003"));
    }

    [Fact]
    public async Task Handle_ARunOverItsCap_ReturnsTheSuggestionsMadeSoFar_AndSaysWhereItStopped_AskingNoMoreThanTheCap()
    {
        Waiting("TO-0000001", "TO-0000002", "TO-0000003", "TO-0000004");
        foreach (var number in new[] { "TO-0000001", "TO-0000002", "TO-0000003", "TO-0000004" })
        {
            Answer(number, SuggestedActionKind.AssignOwnFleet);
        }

        var plan = (await Handler(2).Handle(new PlanTheListQuery(), CancellationToken.None)).Value;

        plan.Suggestions.Select(listed => listed.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002");
        plan.Stopped.Should().Be(new PlanStop(PlanStopReason.CapReached, OrdersLeft: 2, NextOrder: "TO-0000003"));
        _asked.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AListExactlyAsLongAsTheCap_IsNotStopped()
    {
        Waiting("TO-0000001", "TO-0000002");
        Answer("TO-0000001", SuggestedActionKind.AssignOwnFleet);
        Answer("TO-0000002", SuggestedActionKind.AssignOwnFleet);

        var plan = (await Handler(2).Handle(new PlanTheListQuery(), CancellationToken.None)).Value;

        plan.Suggestions.Should().HaveCount(2);
        plan.Stopped.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TheAssistantFailingPartWay_ReturnsWhatItHadMade_AndSaysItStopped()
    {
        Waiting("TO-0000001", "TO-0000002", "TO-0000003");
        Answer("TO-0000001", SuggestedActionKind.AssignOwnFleet);
        _sender.Send(Arg.Is<SuggestAssignmentQuery>(query => query.TransportOrderNumber == "TO-0000002"), Arg.Any<CancellationToken>())
            .Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var plan = (await Handler(10).Handle(new PlanTheListQuery(), CancellationToken.None)).Value;

        plan.Suggestions.Select(listed => listed.TransportOrderNumber).Should().Equal("TO-0000001");
        plan.Stopped.Should().Be(new PlanStop(PlanStopReason.AssistantUnavailable, OrdersLeft: 2, NextOrder: "TO-0000002"));
    }

    [Fact]
    public async Task Handle_AnOrderPlannedMeanwhile_IsSkipped_AndTheRunGoesOn()
    {
        Waiting("TO-0000001", "TO-0000002");
        _sender.Send(Arg.Is<SuggestAssignmentQuery>(query => query.TransportOrderNumber == "TO-0000001"), Arg.Any<CancellationToken>())
            .Returns(Result<Suggestion>.NotFound());
        Answer("TO-0000002", SuggestedActionKind.AssignOwnFleet);

        var plan = (await Handler(10).Handle(new PlanTheListQuery(), CancellationToken.None)).Value;

        plan.Suggestions.Select(listed => listed.TransportOrderNumber).Should().Equal("TO-0000002");
        plan.Stopped.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AnEmptyList_AsksNobody()
    {
        Waiting();

        var plan = (await Handler(10).Handle(new PlanTheListQuery(), CancellationToken.None)).Value;

        plan.Suggestions.Should().BeEmpty();
        plan.Stopped.Should().BeNull();
        _asked.Should().BeEmpty();
    }

    [Fact]
    public void Limits_ARunWithRoomForNoOrder_IsRefused()
    {
        var act = () => new PlanTheListLimits(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private PlanTheListHandler Handler(int cap) => new(_sender, new PlanTheListLimits(cap));

    private void Waiting(params string[] numbers) =>
        _sender.Send(Arg.Any<ListTransportOrdersToPlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TransportOrderToPlanSummary>>([.. numbers.Select(Summary)]));

    private void Answer(string number, SuggestedActionKind action, string? carrierId = null) =>
        _sender.Send(Arg.Is<SuggestAssignmentQuery>(query => query.TransportOrderNumber == number), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _asked.Add(call.Arg<SuggestAssignmentQuery>());
                return new ValueTask<Result<Suggestion>>(new Suggestion("Because.", [], $"Do {action}.", Confidence.High, action, new AgentRun("planner", $"run-{number}"), carrierId));
            });

    private static TransportOrderToPlanSummary Summary(string number) =>
        new(number, default, new ShipperView("SHP-0001", "Brouwer Dranken"), null, null, null, null, null, DateTimeOffset.UnixEpoch);
}
