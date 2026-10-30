using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Application.Run;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.HealthCheck.Application.UnitTests.Run;

public sealed class RunHealthCheckHandlerTests
{
    private readonly IEstateConfiguration _estate = Substitute.For<IEstateConfiguration>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    private readonly RecentReport _recent = new();

    private RunHealthCheckHandler Handler() => new(_estate, Checklist.Default, _recent, _time);

    [Fact]
    public async Task Handle_OverTheAsFoundEstate_ReportsTheFinding()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>()).Returns(Estates.WithoutAlerts("carrier-tracking"));

        var result = await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Estate.Should().Be("as-found");
        result.Value.ChecksRun.Should().Be(Checklist.Default.Checks.Count);
        result.Value.RanAt.Should().Be(_time.GetUtcNow());
        result.Value.Findings.Should().ContainSingle().Which.CheckId.Should().Be("DLQ-ALERT");
    }

    [Fact]
    public async Task Handle_PassesTheProgressToTheRead_AndSaysWhenItRunsTheChecks()
    {
        var steps = new List<string>();
        _estate.ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>()).Returns(Estates.Fixed);

        await Handler().Handle(new RunHealthCheckQuery(new Steps(steps)), TestContext.Current.CancellationToken);

        await _estate.Received().ReadAsync(Arg.Any<CancellationToken>(), Arg.Is<IProgress<string>?>(progress => progress is Steps));
        steps.Should().Equal($"Running the {Checklist.Default.Checks.Count} Checks over what was read");
    }

    // Progress<T> posts to a context of its own; a test wants the steps as they are reported.
    private sealed class Steps(List<string> steps) : IProgress<string>
    {
        public void Report(string value) => steps.Add(value);
    }

    [Fact]
    public async Task Handle_OverTheFixedEstate_ReportsNoFinding_ButSaysHowManyChecksRan()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>()).Returns(Estates.Fixed);

        var result = await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);

        result.Value.Findings.Should().BeEmpty();
        result.Value.ChecksRun.Should().Be(Checklist.Default.Checks.Count, because: "no Finding must be told from nothing having run");
    }

    [Fact]
    public async Task Handle_WithoutReuse_ReadsTheEstateAfreshEveryTime()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>()).Returns(Estates.Fixed);

        await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);
        await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);

        await _estate.Received(2).ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>());
    }

    [Fact]
    public async Task Handle_WithReuse_AfterARun_ReturnsThatRunsReport_WithoutReadingTheEstate()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>()).Returns(Estates.WithoutAlerts("carrier-tracking"));
        var page = await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(30));

        var agent = await Handler().Handle(new RunHealthCheckQuery(ReuseWithin: TimeSpan.FromSeconds(60)), TestContext.Current.CancellationToken);

        agent.Value.Should().BeSameAs(page.Value);
        await _estate.Received(1).ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>());
    }

    [Fact]
    public async Task Handle_WithReuse_OfAReportOlderThanTheLimit_ReadsTheEstateAgain()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>()).Returns(Estates.Fixed);
        await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(61));

        var agent = await Handler().Handle(new RunHealthCheckQuery(ReuseWithin: TimeSpan.FromSeconds(60)), TestContext.Current.CancellationToken);

        agent.Value.RanAt.Should().Be(_time.GetUtcNow());
        await _estate.Received(2).ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>());
    }

    [Fact]
    public async Task Handle_WithReuse_WithNoReportYet_ReadsTheEstate_AndKeepsTheReportForThePagesNextCaller()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>()).Returns(Estates.Fixed);
        var reuse = new RunHealthCheckQuery(ReuseWithin: TimeSpan.FromSeconds(60));

        await Handler().Handle(reuse, TestContext.Current.CancellationToken);
        await Handler().Handle(reuse, TestContext.Current.CancellationToken);

        await _estate.Received(1).ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>());
    }

    [Fact]
    public async Task Handle_WithReuse_TwoAtOnce_ReadTheEstateOnce()
    {
        var read = new TaskCompletionSource<EstateConfiguration>();
        _estate.ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>()).Returns(_ => read.Task);
        var reuse = new RunHealthCheckQuery(ReuseWithin: TimeSpan.FromSeconds(60));

        var first = Handler().Handle(reuse, TestContext.Current.CancellationToken).AsTask();
        var second = Handler().Handle(reuse, TestContext.Current.CancellationToken).AsTask();
        read.SetResult(Estates.Fixed);
        await Task.WhenAll(first, second);

        await _estate.Received(1).ReadAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<string>?>());
        second.Result.Value.Should().BeSameAs(first.Result.Value);
    }
}
