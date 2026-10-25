using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.Application.UnitTests.Checks;

public sealed class DeadLetterAlertCheckTests
{
    private readonly DeadLetterAlertCheck _check = new();

    [Fact]
    public void Examine_WhenEveryEntityHasAnAlert_FindsNothing() =>
        _check.Examine(Estates.Fixed).Should().BeEmpty();

    [Fact]
    public void Examine_NamesEachEntityWithoutAnAlert_InNameOrder()
    {
        var evidence = _check.Examine(Estates.WithoutAlerts("carrier-tracking", "carrier-booking"));

        evidence.Select(item => item.Subject).Should().Equal("carrier-booking", "carrier-tracking");
        evidence.Should().OnlyContain(item => item.Detail.Contains(item.Subject, StringComparison.Ordinal));
    }

    [Fact]
    public void Examine_AnEstateWithNoEntities_FindsNothing() =>
        _check.Examine(new("empty", [])).Should().BeEmpty();

    [Fact]
    public void TheCheck_HasAnIdARuleASeverityTheToggleItMapsToAndAFix()
    {
        _check.Id.Should().Be("DLQ-ALERT");
        _check.Rule.Should().NotBeNullOrWhiteSpace();
        _check.Severity.Should().Be(Severity.High);
        _check.AsFoundToggle.Should().Be("entitiesWithoutDeadLetterAlert", because: "it is the as-found toggle that removes these alerts (infra/estate.bicep)");
        _check.Fix.Should().Contain("entitiesWithoutDeadLetterAlert");
    }
}
