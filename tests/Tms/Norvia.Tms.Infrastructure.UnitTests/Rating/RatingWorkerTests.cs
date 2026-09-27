using System.Collections.Concurrent;
using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Rating;
using NSubstitute.ExceptionExtensions;

namespace Norvia.Tms.Infrastructure.UnitTests.Rating;

/// <summary>
/// The rating worker's sweep and the tracking-contract keys it traces each rating under (coding guidelines §7), with the
/// mediator substituted and time faked. The worker's first sweep runs as soon as it starts.
/// </summary>
public sealed class RatingWorkerTests : IAsyncDisposable
{
    private static readonly TransportOrderToRate Order = new("TO-0000007", "SHP-0001", "corr-intake");
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ActivityListener _listener;
    private readonly ServiceProvider _services;
    private readonly RatingSignal _signal;
    private readonly RatingWorker _worker;

    public RatingWorkerTests()
    {
        // A source of this test's own, so ratings traced by tests running alongside aren't seen here.
        var sourceName = $"Norvia.Tms.Infrastructure.UnitTests.{Guid.NewGuid():N}";
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);

        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns(sourceName);
        var options = Options.Create(new RatingOptions { SweepInterval = SweepInterval, BatchSize = 2 });
        _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs));
        _services = new ServiceCollection().AddSingleton(_mediator).BuildServiceProvider();
        _signal = new RatingSignal(options);
        _worker = new RatingWorker(
            _services.GetRequiredService<IServiceScopeFactory>(), _signal, _time, environment, options, _loggerFactory.CreateLogger<RatingWorker>());

        Waiting(Batch(Order));
    }

    [Fact]
    public async Task ExecuteAsync_OrderRated_RecordsTheOutcomeSucceeded_WithoutAFailureCode()
    {
        Rating(Result.Success(Answer(RatingResult.Rated)));

        await _worker.StartAsync(_cancellationToken);

        var rated = await LogAsync(log => log.Message.Contains("Rated", StringComparison.Ordinal));
        ScopeKeysOf(rated).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-intake",
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task ExecuteAsync_OrderRated_TagsItsTraceSucceeded()
    {
        Rating(Result.Success(Answer(RatingResult.Rated)));

        await _worker.StartAsync(_cancellationToken);

        var trace = await RatingTraceAsync();
        trace.GetTagItem("outcome").Should().Be("Succeeded");
        trace.GetTagItem("failureCode").Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_RatingFailed_RecordsTheOutcomeSucceeded_WithTheFailureCode()
    {
        // Rating failed is Rating's answer, not a failed flow: the order stays Accepted and nothing is tried again (P2).
        Rating(Result.Success(Answer(RatingResult.RatingFailed, new RatingFailureView("NoMatchingLane", "No Lane."))));

        await _worker.StartAsync(_cancellationToken);

        var failed = await LogAsync(log => log.Level == LogLevel.Warning);
        failed.Message.Should().Contain("NoMatchingLane");
        ScopeKeysOf(failed).Should().Contain("outcome", "Succeeded").And.Contain("failureCode", "NoMatchingLane");
    }

    [Fact]
    public async Task ExecuteAsync_RatingFailed_TagsItsTraceSucceeded_WithTheFailureCode()
    {
        Rating(Result.Success(Answer(RatingResult.RatingFailed, new RatingFailureView("NoMatchingLane", "No Lane."))));

        await _worker.StartAsync(_cancellationToken);

        var trace = await RatingTraceAsync();
        trace.GetTagItem("outcome").Should().Be("Succeeded");
        trace.GetTagItem("failureCode").Should().Be("NoMatchingLane");
        trace.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RatingAnsweredWithoutARatingResult_RecordsTheOutcomeRetrying()
    {
        Rating(Result.Error("The Transport Order couldn't be read."));

        await _worker.StartAsync(_cancellationToken);

        var notRated = await LogAsync(log => log.Message.Contains("stays Not yet rated", StringComparison.Ordinal));
        ScopeKeysOf(notRated).Should().Contain("outcome", "Retrying").And.Contain("transportOrderNumber", "TO-0000007");
        (await RatingTraceAsync()).GetTagItem("outcome").Should().Be("Retrying");
    }

    [Fact]
    public async Task ExecuteAsync_RatingThrows_RecordsTheOutcomeRetrying()
    {
        RatingThrows(new InvalidOperationException("The database went away."));

        await _worker.StartAsync(_cancellationToken);

        var threw = await LogAsync(log => log.Level == LogLevel.Error);
        threw.Exception.Should().BeOfType<InvalidOperationException>();
        ScopeKeysOf(threw).Should().Contain("outcome", "Retrying").And.Contain("correlationId", "corr-intake");
    }

    [Fact]
    public async Task ExecuteAsync_RatingThrows_TagsItsTraceRetrying_AsAnError()
    {
        RatingThrows(new InvalidOperationException("The database went away."));

        await _worker.StartAsync(_cancellationToken);

        var trace = await RatingTraceAsync();
        trace.GetTagItem("outcome").Should().Be("Retrying");
        trace.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RatedByAnotherInstanceFirst_RecordsNoOutcome_AsThatInstanceDoes()
    {
        RatingThrows(new DbUpdateConcurrencyException("The row changed."));

        await _worker.StartAsync(_cancellationToken);

        var elsewhere = await LogAsync(log => log.Message.Contains("another TMS instance", StringComparison.Ordinal));
        ScopeKeysOf(elsewhere).Should().NotContainKey("outcome");
    }

    [Fact]
    public async Task ExecuteAsync_ListingTheOrdersToRateThrows_LogsItAndRatesNothing()
    {
        _mediator.Send(Arg.Any<ListTransportOrdersNotYetRatedQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No database."));

        await _worker.StartAsync(_cancellationToken);

        var failed = await LogAsync(log => log.Message.Contains("Looking for Transport Orders to rate failed", StringComparison.Ordinal));
        failed.Exception.Should().BeOfType<InvalidOperationException>();
        await _mediator.DidNotReceive().Send(Arg.Any<RateTransportOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AFullBatch_AsksForTheNextPassingOverTheOrdersItCouldNotRate()
    {
        var second = new TransportOrderToRate("TO-0000008", "SHP-0001", "corr-second");
        Waiting(Batch(Order, second), Batch());
        RatingThrows(new InvalidOperationException("The database went away."), Order.TransportOrderNumber);
        Rating(Result.Success(Answer(RatingResult.Rated)), second.TransportOrderNumber);

        await _worker.StartAsync(_cancellationToken);

        await EventuallyAsync(() => ListQueries().Count() >= 2);
        ListQueries().Should().Equal(new ListTransportOrdersNotYetRatedQuery(0, 2), new ListTransportOrdersNotYetRatedQuery(1, 2));
    }

    [Fact]
    public async Task ExecuteAsync_Triggered_SweepsAgain()
    {
        Waiting(Batch(), Batch(Order));
        Rating(Result.Success(Answer(RatingResult.Rated)));
        await _worker.StartAsync(_cancellationToken);
        await EventuallyAsync(() => ListQueries().Any());

        _signal.Trigger();

        await LogAsync(log => log.Message.Contains("TO-0000007 Rated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_SweepIntervalPassed_SweepsAgain()
    {
        Waiting(Batch(), Batch(Order));
        Rating(Result.Success(Answer(RatingResult.Rated)));
        await _worker.StartAsync(_cancellationToken);
        await EventuallyAsync(() => ListQueries().Any());

        _time.Advance(SweepInterval);

        await LogAsync(log => log.Message.Contains("TO-0000007 Rated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trigger_WithoutTriggerOnAcceptance_LeavesTheWorkerWaiting()
    {
        var signal = new RatingSignal(Options.Create(new RatingOptions { TriggerOnAcceptance = false }));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);

        signal.Trigger();

        var waiting = signal.WaitAsync(stop.Token).AsTask();
        waiting.IsCompleted.Should().BeFalse();
        await stop.CancelAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
        await _services.DisposeAsync();
        _listener.Dispose();
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private static RateTransportOrderResult Answer(RatingResult result, RatingFailureView? failure = null) =>
        new(result, failure, Order.ShipperId, Order.CorrelationId);

    private static TransportOrderToRate[] Batch(params TransportOrderToRate[] orders) => orders;

    private void Waiting(IReadOnlyList<TransportOrderToRate> first, params IReadOnlyList<TransportOrderToRate>[] then) =>
        _mediator.Send(Arg.Any<ListTransportOrdersNotYetRatedQuery>(), Arg.Any<CancellationToken>()).Returns(first, then);

    private void Rating(Result<RateTransportOrderResult> result, string? transportOrderNumber = null) =>
        _mediator.Send(ForOrder(transportOrderNumber), Arg.Any<CancellationToken>()).Returns(result);

    private void RatingThrows(Exception exception, string? transportOrderNumber = null) =>
        _mediator.Send(ForOrder(transportOrderNumber), Arg.Any<CancellationToken>()).ThrowsAsync(exception);

    private static RateTransportOrderCommand ForOrder(string? transportOrderNumber) =>
        transportOrderNumber is null
            ? Arg.Any<RateTransportOrderCommand>()
            : Arg.Is<RateTransportOrderCommand>(command => command.TransportOrderNumber == transportOrderNumber);

    private IEnumerable<ListTransportOrdersNotYetRatedQuery> ListQueries() =>
        _mediator.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<ListTransportOrdersNotYetRatedQuery>();

    private async Task<FakeLogRecord> LogAsync(Func<FakeLogRecord, bool> match)
    {
        await EventuallyAsync(() => _logs.Collector.GetSnapshot().Any(match));
        return _logs.Collector.GetSnapshot().First(match);
    }

    private async Task<Activity> RatingTraceAsync()
    {
        await EventuallyAsync(() => _activities.Any(activity => activity.OperationName == "Rate Transport Order"));
        return _activities.First(activity => activity.OperationName == "Rate Transport Order");
    }

    // The worker runs on its own; what it did shows up within moments.
    private async Task EventuallyAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static Dictionary<string, object?> ScopeKeysOf(FakeLogRecord log) =>
        log.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .GroupBy(pair => pair.Key)
            .ToDictionary(pairs => pairs.Key, pairs => pairs.Last().Value);
}
