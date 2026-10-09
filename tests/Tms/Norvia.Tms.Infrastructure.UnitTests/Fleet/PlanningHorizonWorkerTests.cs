using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Fleet.Planning.OpenDays;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.Fleet;

namespace Norvia.Tms.Infrastructure.UnitTests.Fleet;

/// <summary>
/// The horizon job's schedule (P10): it runs when the TMS starts and at the first look after 00:00 Europe/Amsterdam, not
/// again on the same day, and logs each run. Time is faked; the mediator is substituted.
/// </summary>
public sealed class PlanningHorizonWorkerTests : IAsyncDisposable
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(2)));
    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServiceProvider _services;
    private readonly PlanningHorizonWorker _worker;

    public PlanningHorizonWorkerTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Tms.Infrastructure.UnitTests");
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _services = new ServiceCollection().AddSingleton(_mediator).BuildServiceProvider();
        _mediator.Send(Arg.Any<OpenPlanningDaysCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OpenPlanningDaysResult(new DateOnly(2026, 10, 12), [new DateOnly(2026, 10, 26)], 1));
        _worker = new PlanningHorizonWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new PlanningHorizonRules(14, Amsterdam),
            _time,
            environment,
            Options.Create(new PlanningHorizonOptions { CheckInterval = CheckInterval }),
            _loggerFactory.CreateLogger<PlanningHorizonWorker>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheTmsStarts_RunsTheJobAtOnce_AndLogsTheRun()
    {
        await _worker.StartAsync(_cancellationToken);

        var ran = await LogAsync(log => log.Level == LogLevel.Information);
        ran.Message.Should().Contain("opened 1 days").And.Contain("closed 1");
        Runs().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_LooksAgainOnTheSameDay_RunsNothingMore()
    {
        await _worker.StartAsync(_cancellationToken);
        await EventuallyAsync(() => Runs() == 1);

        _time.Advance(CheckInterval);
        _time.Advance(CheckInterval);
        await Task.Delay(TimeSpan.FromMilliseconds(100), _cancellationToken);

        Runs().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_AfterMidnightInAmsterdam_RunsTheJobAgain()
    {
        await _worker.StartAsync(_cancellationToken);
        await EventuallyAsync(() => Runs() == 1);

        // 00:00:30 on the 13th in Amsterdam, which the next look after midnight finds.
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 0, 0, 30, TimeSpan.FromHours(2)));
        _time.Advance(CheckInterval);

        await EventuallyAsync(() => Runs() == 2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRunFails_LogsItAsRetrying_AndTriesAgainAtTheNextLook()
    {
        var calls = 0;
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<OpenPlanningDaysCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? throw new InvalidOperationException("database down")
                : ValueTask.FromResult(new OpenPlanningDaysResult(new DateOnly(2026, 10, 12), [], 0)));
#pragma warning restore CA2012

        await _worker.StartAsync(_cancellationToken);

        var failed = await LogAsync(log => log.Level == LogLevel.Error);
        failed.Message.Should().Contain("failed for 2026-10-12").And.Contain("retrying");
        failed.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope)
            .Should().Contain(new KeyValuePair<string, object?>("outcome", "Retrying"));

        _time.Advance(CheckInterval);

        await EventuallyAsync(() => Runs() == 2);
        (await LogAsync(log => log.Level == LogLevel.Information)).Message.Should().Contain("opened 0 days");
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
        await _services.DisposeAsync();
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private int Runs() => _mediator.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<OpenPlanningDaysCommand>().Count();

    private async Task<FakeLogRecord> LogAsync(Func<FakeLogRecord, bool> match)
    {
        await EventuallyAsync(() => _logs.Collector.GetSnapshot().Any(match));
        return _logs.Collector.GetSnapshot().First(match);
    }

    private async Task EventuallyAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}
