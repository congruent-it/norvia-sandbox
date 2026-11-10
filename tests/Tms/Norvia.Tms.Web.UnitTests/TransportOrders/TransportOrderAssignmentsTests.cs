using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Agents;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class TransportOrderAssignmentsTests
{
    private readonly ITransportOrderCommands _commands = Substitute.For<ITransportOrderCommands>();
    private readonly TransportOrderAssignments _assignments;

    public TransportOrderAssignmentsTests()
    {
        _assignments = new TransportOrderAssignments(_commands, NullLogger.Instance);
    }

    [Fact]
    public async Task AssignEachAsync_SeveralOrders_AssignsEachOnItsOwnInTheOrderGiven()
    {
        _commands.AssignAsync("TO-0000042", "CAR-0002", Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        _commands.AssignAsync("TO-0000043", "CAR-0002", Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Refused("Transport Order TO-0000043 is Planned already."));
        _commands.AssignAsync("TO-0000044", "CAR-0002", Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000013"));

        var answers = await AssignEachAsync(["TO-0000042", "TO-0000043", "TO-0000044"], "CAR-0002");

        answers.Should().Equal(
            ("TO-0000042", new AssignAnswer.Planned("SH-0000012")),
            ("TO-0000043", new AssignAnswer.Refused("Transport Order TO-0000043 is Planned already.")),
            ("TO-0000044", new AssignAnswer.Planned("SH-0000013")));
    }

    [Fact]
    public async Task AssignEachAsync_ASuggestedAssignment_NamesTheRunForThatOrderOnly()
    {
        var run = new AgentRun("planner", "run-7");
        _commands.AssignAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        var suggested = new SuggestedAssignment("TO-0000042", "CAR-0002", run);

        await foreach (var _ in _assignments.AssignEachAsync(["TO-0000042", "TO-0000043"], "CAR-0002", [suggested], Xunit.TestContext.Current.CancellationToken))
        {
        }

        await _commands.Received(1).AssignAsync("TO-0000042", "CAR-0002", run, Arg.Any<CancellationToken>());
        await _commands.Received(1).AssignAsync("TO-0000043", "CAR-0002", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignEachAsync_WhenTheTmsCantBeReachedForOne_AnswersNullForItAndAssignsTheOthers()
    {
        _commands.AssignAsync("TO-0000042", null, Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>())
            .Returns<AssignAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));
        _commands.AssignAsync("TO-0000043", null, Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000013"));

        var answers = await AssignEachAsync(["TO-0000042", "TO-0000043"], carrierId: null);

        answers.Should().Equal(("TO-0000042", (AssignAnswer?)null), ("TO-0000043", new AssignAnswer.Planned("SH-0000013")));
    }

    [Fact]
    public async Task AssignEachAsync_WhenCancelled_StopsWithoutAssigningTheRest()
    {
        using var closed = new CancellationTokenSource();
        _commands.AssignAsync("TO-0000042", null, Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await closed.CancelAsync();
            return (AssignAnswer)new AssignAnswer.Planned("SH-0000012");
        });

        var answers = new List<(string, AssignAnswer?)>();
        await foreach (var answer in _assignments.AssignEachAsync(["TO-0000042", "TO-0000043"], null, [], closed.Token))
        {
            answers.Add(answer);
        }

        answers.Should().BeEmpty();
        await _commands.DidNotReceive().AssignAsync("TO-0000043", Arg.Any<string?>(), Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>());
    }

    private async Task<List<(string, AssignAnswer?)>> AssignEachAsync(IReadOnlyList<string> numbers, string? carrierId)
    {
        var answers = new List<(string, AssignAnswer?)>();
        await foreach (var answer in _assignments.AssignEachAsync(numbers, carrierId, [], Xunit.TestContext.Current.CancellationToken))
        {
            answers.Add(answer);
        }

        return answers;
    }
}
