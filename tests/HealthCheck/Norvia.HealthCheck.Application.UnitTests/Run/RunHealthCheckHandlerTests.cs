using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Application.Run;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.HealthCheck.Application.UnitTests.Run;

public sealed class RunHealthCheckHandlerTests
{
    private readonly IEstateConfiguration _estate = Substitute.For<IEstateConfiguration>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    private RunHealthCheckHandler Handler() => new(_estate, Checklist.Default, _time);

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
}
