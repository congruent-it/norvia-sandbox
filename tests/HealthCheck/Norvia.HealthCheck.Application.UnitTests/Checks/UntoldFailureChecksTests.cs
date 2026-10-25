using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.UnitTests.Checks;

/// <summary>A failure and a missing alert together are a Finding of their own: it failed and nobody was told (#395).</summary>
public sealed class UntoldFailureChecksTests
{
    private readonly UntoldFailedRunsCheck _failedRuns = new();
    private readonly UntoldDeadLettersCheck _deadLetters = new();

    private static EstateConfiguration Tracking(bool alert, int failedRuns, int deadLettered) =>
        new("test", [new("carrier-tracking", alert, deadLettered)]) { Flows = [new("carrier-tracking", alert, FailedRuns: failedRuns)] };

    [Fact]
    public void AFailingFlowWithoutAnAlert_GivesTheFindingWithTheFailuresAsEvidence()
    {
        var evidence = _failedRuns.Examine(Tracking(alert: false, failedRuns: 14, deadLettered: 0)).Should().ContainSingle().Subject;

        evidence.Subject.Should().Be("carrier-tracking");
        evidence.Detail.Should().Be("The carrier-tracking Logic App failed 14 times in the last 4 days and has no failed-run alert.");
    }

    [Fact]
    public void AFailingFlowWithAnAlert_GivesNoFinding() =>
        _failedRuns.Examine(Tracking(alert: true, failedRuns: 14, deadLettered: 0)).Should().BeEmpty();

    [Fact]
    public void AFlowWithoutAnAlertThatNeverFailed_GivesNoUntoldFinding_OnlyTheAlertChecksOwn()
    {
        var estate = Tracking(alert: false, failedRuns: 0, deadLettered: 0);

        _failedRuns.Examine(estate).Should().BeEmpty();
        Checklist.Default.Run(estate).Select(finding => finding.CheckId).Should().Equal("DLQ-ALERT", "FAILED-RUN-ALERT");
    }

    [Fact]
    public void AFlowWhoseFailuresCouldNotBeRead_GivesNoFinding() =>
        _failedRuns.Examine(new("test", []) { Flows = [new("carrier-tracking", false, FailedRuns: null)] }).Should().BeEmpty();

    [Fact]
    public void OneFailedRun_IsSaidInTheSingular() =>
        _failedRuns.Examine(Tracking(alert: false, failedRuns: 1, deadLettered: 0)).Single().Detail.Should().Contain("failed 1 time in");

    [Fact]
    public void ADeadLetteredQueueWithoutAnAlert_GivesTheFindingWithTheCountAsEvidence()
    {
        var evidence = _deadLetters.Examine(Tracking(alert: false, failedRuns: 0, deadLettered: 3)).Should().ContainSingle().Subject;

        evidence.Subject.Should().Be("carrier-tracking");
        evidence.Detail.Should().Be("The dead-letter queue of carrier-tracking holds 3 messages and no alert watches it.");
    }

    [Fact]
    public void ADeadLetteredQueueWithAnAlert_GivesNoFinding() =>
        _deadLetters.Examine(Tracking(alert: true, failedRuns: 0, deadLettered: 3)).Should().BeEmpty();

    [Fact]
    public void AnEmptyQueueWithoutAnAlert_AndOneWhoseCountCouldNotBeRead_GiveNoFinding() =>
        _deadLetters.Examine(new("test", [new("billing", false, 0), new("carrier-booking", false, null)])).Should().BeEmpty();

    [Fact]
    public void WithTheAlertOn_TheSameFailuresGiveNoNobodyWasToldFinding()
    {
        var findings = Checklist.Default.Run(Tracking(alert: true, failedRuns: 14, deadLettered: 3));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void WithTheAlertOff_TheFailuresGiveTheCriticalFindings_BeforeTheAlertChecks()
    {
        var findings = Checklist.Default.Run(Tracking(alert: false, failedRuns: 14, deadLettered: 3));

        findings.Select(finding => (finding.CheckId, finding.Severity)).Should().Equal(
            ("DLQ-UNTOLD", Severity.Critical), ("FAILED-RUNS-UNTOLD", Severity.Critical), ("DLQ-ALERT", Severity.High), ("FAILED-RUN-ALERT", Severity.High));
    }

    [Fact]
    public void TheChecks_ShareTheTogglesOfTheAlertChecksTheyPair()
    {
        _failedRuns.AsFoundToggle.Should().Be(new FailedRunAlertCheck().AsFoundToggle);
        _deadLetters.AsFoundToggle.Should().Be(new DeadLetterAlertCheck().AsFoundToggle);
        _failedRuns.Fix.Should().Contain("flowsWithoutFailedRunAlert");
        _deadLetters.Fix.Should().Contain("entitiesWithoutDeadLetterAlert");
    }
}
