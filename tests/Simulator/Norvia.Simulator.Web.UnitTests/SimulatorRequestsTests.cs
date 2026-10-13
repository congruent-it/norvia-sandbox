using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Log.List;
using Norvia.Simulator.Application.Plans.Preview;
using Norvia.Simulator.Application.Runs.Cancel;
using Norvia.Simulator.Application.Runs.ClearHistory;
using Norvia.Simulator.Application.Runs.List;
using Norvia.Simulator.Application.Runs.Open;
using Norvia.Simulator.Application.Runs.Start;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.UnitTests;

public sealed class SimulatorRequestsTests
{
    private static readonly SimulationSettings Settings = new(5, 10, 30, 1, RunMode.Burst, TimeSpan.Zero);
    private static readonly SimulationRunId RunId = SimulationRunId.New();

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly List<IServiceScope> _scopes = [];
    private readonly SimulatorRequests _requests;

    public SimulatorRequestsTests()
    {
        var services = new ServiceCollection().AddScoped(_ => _mediator).BuildServiceProvider();
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(_ =>
        {
            var scope = services.CreateScope();
            _scopes.Add(scope);
            return scope;
        });
        _requests = new SimulatorRequests(factory);
    }

    [Fact]
    public async Task EachRequest_IsSentThroughTheMediator()
    {
        await _requests.GetConfigureAsync(CancellationToken.None);
        await _requests.PreviewAsync(Settings, CancellationToken.None);
        await _requests.StartAsync(Settings, "Dev", CancellationToken.None);
        await _requests.CancelAsync(RunId, CancellationToken.None);
        await _requests.ClearHistoryAsync(CancellationToken.None);
        await _requests.ListRunsAsync(CancellationToken.None);
        await _requests.GetRunAsync(RunId, CancellationToken.None);
        await _requests.ListLogAsync(RunId, new LogFilter(), CancellationToken.None);

        var sent = _mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).ToList();
        sent.Select(message => message!.GetType()).Should().Equal(
            typeof(GetConfigureQuery), typeof(PreviewPlanQuery), typeof(StartRunCommand), typeof(CancelRunCommand),
            typeof(ClearHistoryCommand), typeof(ListRunsQuery), typeof(GetRunQuery), typeof(ListLogQuery));
        sent.OfType<StartRunCommand>().Single().Should().Be(new StartRunCommand(Settings, "Dev"));
    }

    [Fact]
    public async Task EachRequest_GetsAScopeOfItsOwn_WhichEndsWithIt()
    {
        await _requests.ListRunsAsync(CancellationToken.None);
        await _requests.ListRunsAsync(CancellationToken.None);

        _scopes.Should().HaveCount(2);
    }
}
