// NSubstitute sets up a ValueTask-returning mediator call by calling it, which the analyzer reads as a ValueTask left unawaited.
#pragma warning disable CA2012
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Simulator.Application.Runs.Abandon;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Infrastructure.Execution;

namespace Norvia.Simulator.Infrastructure.UnitTests.Execution;

public sealed class RunExecutorTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ServiceProvider _services;

    public RunExecutorTests()
    {
        _services = new ServiceCollection().AddSingleton(_mediator).BuildServiceProvider();
    }

    private RunExecutor Executor() => new(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<RunExecutor>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition never held.");
            }

            await Task.Yield();
        }
    }

    private int Executed() => _mediator.ReceivedCalls().Count(call => call.GetArguments()[0] is ExecuteRunCommand);

    [Fact]
    public async Task Start_ARun_IsExecutedInTheBackground_AfterAbandoningRunsLeftOverFromBefore()
    {
        var executor = Executor();
        await executor.StartAsync(TestContext.Current.CancellationToken);
        var runId = SimulationRunId.New();

        executor.Start(runId);
        await WaitUntilAsync(() => Executed() == 1);
        await executor.StopAsync(TestContext.Current.CancellationToken);

        var commands = _mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).ToList();
        commands[0].Should().BeOfType<AbandonActiveRunsCommand>();
        commands.OfType<ExecuteRunCommand>().Should().ContainSingle().Which.RunId.Should().Be(runId);
    }

    [Fact]
    public async Task Cancel_ARunBeingExecuted_SignalsItToStopSending()
    {
        var stopSignal = new TaskCompletionSource<CancellationToken>();
        _mediator.Send(Arg.Any<ExecuteRunCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            stopSignal.SetResult(call.Arg<ExecuteRunCommand>().StopSending);
            return ValueTask.FromResult(Ardalis.Result.Result.Success());
        });
        var executor = Executor();
        await executor.StartAsync(TestContext.Current.CancellationToken);
        var runId = SimulationRunId.New();

        executor.Start(runId);
        var token = await stopSignal.Task;
        token.IsCancellationRequested.Should().BeFalse();
        executor.Cancel(runId);
        executor.Cancel(SimulationRunId.New());

        await executor.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Start_ARunThatThrows_FailsTheRunAndTheNextOneStillRuns()
    {
        _mediator.Send(Arg.Any<ExecuteRunCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"), _ => ValueTask.FromResult(Ardalis.Result.Result.Success()));
        var executor = Executor();
        await executor.StartAsync(TestContext.Current.CancellationToken);

        executor.Start(SimulationRunId.New());
        executor.Start(SimulationRunId.New());
        await WaitUntilAsync(() => Executed() == 2);
        await executor.StopAsync(TestContext.Current.CancellationToken);

        _mediator.ReceivedCalls().Count(call => call.GetArguments()[0] is AbandonActiveRunsCommand).Should().Be(2, "once at startup and once for the run that threw");
    }
}
