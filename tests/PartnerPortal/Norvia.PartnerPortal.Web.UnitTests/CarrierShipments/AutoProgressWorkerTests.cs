using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class AutoProgressWorkerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly ServiceProvider _services;
    private readonly AutoProgressWorker _worker;

    public AutoProgressWorkerTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _mediator).BuildServiceProvider();
        _worker = new AutoProgressWorker(_services.GetRequiredService<IServiceScopeFactory>(), _time, NullLogger<AutoProgressWorker>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
        await _services.DisposeAsync();
    }

    private int Sweeps => _mediator.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IMediator.Send));

    private void NextDueAfterEachSweep(params DateTimeOffset?[] nextDue)
    {
        var sweep = 0;
        _mediator.Send(Arg.Any<SendDueTrackingCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => new AutoProgressOutcome(nextDue[Math.Min(sweep++, nextDue.Length - 1)]));
    }

    private static void Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        condition().Should().BeTrue();
    }

    [Fact]
    public async Task Start_AsksForTheDueEventsAtOnce_AndThenWaitsWhenNothingIsScheduled()
    {
        NextDueAfterEachSweep((DateTimeOffset?)null);

        await _worker.StartAsync(CancellationToken.None);
        Eventually(() => Sweeps == 1);
        _time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        Sweeps.Should().Be(1, "nothing is scheduled, so nothing is polled");
    }

    [Fact]
    public async Task AConfirmation_WakesTheWorker()
    {
        NextDueAfterEachSweep((DateTimeOffset?)null);
        await _worker.StartAsync(CancellationToken.None);
        Eventually(() => Sweeps == 1);

        _worker.ShipmentConfirmed();

        Eventually(() => Sweeps == 2);
    }

    [Fact]
    public async Task WhenTheNextEventIsDue_AsksAgain()
    {
        NextDueAfterEachSweep(Start.AddSeconds(5), (DateTimeOffset?)null);
        await _worker.StartAsync(CancellationToken.None);
        Eventually(() => Sweeps == 1);
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromSeconds(5));

        Eventually(() => Sweeps == 2);
    }

    [Fact]
    public async Task WhenTheSweepFails_TriesAgainShortly()
    {
        var attempts = 0;
        _mediator.Send(Arg.Any<SendDueTrackingCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
            ++attempts == 1 ? throw new InvalidOperationException("The database is resuming.") : new AutoProgressOutcome(null));
        await _worker.StartAsync(CancellationToken.None);
        Eventually(() => Sweeps == 1);
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        _time.Advance(AutoProgressWorker.RetryAfter);

        Eventually(() => Sweeps == 2);
    }
}
