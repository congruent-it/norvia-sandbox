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
    public async Task Handle_OverTheFixedEstate_ReportsNoFinding_ButSaysHowManyChecksRan()
    {
        _estate.ReadAsync(Arg.Any<CancellationToken>()).Returns(Estates.Fixed);

        var result = await Handler().Handle(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);

        result.Value.Findings.Should().BeEmpty();
        result.Value.ChecksRun.Should().Be(Checklist.Default.Checks.Count, because: "no Finding must be told from nothing having run");
    }
}
