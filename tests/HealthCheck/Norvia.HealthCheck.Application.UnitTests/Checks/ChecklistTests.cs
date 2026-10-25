using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.UnitTests.Checks;

public sealed class ChecklistTests
{
    [Fact]
    public void Run_OverTheFixedEstate_GivesNoFinding() =>
        Checklist.Default.Run(Estates.Fixed).Should().BeEmpty();

    [Fact]
    public void Run_OverAnEstateWithMissingAlerts_GivesOneFindingWithTheChecksOwnSeverityRuleToggleAndFix()
    {
        var check = new DeadLetterAlertCheck();

        var finding = Checklist.Default.Run(Estates.WithoutAlerts("carrier-tracking", "carrier-booking")).Should().ContainSingle().Subject;

        finding.Id.Should().Be("F-DLQ-ALERT");
        finding.CheckId.Should().Be(check.Id);
        finding.Severity.Should().Be(check.Severity);
        finding.Rule.Should().Be(check.Rule);
        finding.Title.Should().Be(check.Title);
        finding.AsFoundToggle.Should().Be(check.AsFoundToggle);
        finding.Fix.Should().Be(check.Fix);
        finding.Evidence.Select(evidence => evidence.Subject).Should().Equal("carrier-booking", "carrier-tracking");
    }

    [Fact]
    public void Run_OrdersFindingsMostSevereFirst_ThenById()
    {
        var checklist = new Checklist([new FakeCheck("B-LOW", Severity.Low), new FakeCheck("C-CRIT", Severity.Critical), new FakeCheck("A-LOW", Severity.Low), new FakeCheck("D-PASS", Severity.High, passes: true)]);

        var findings = checklist.Run(Estates.Fixed);

        findings.Select(finding => finding.Id).Should().Equal("F-C-CRIT", "F-A-LOW", "F-B-LOW");
    }

    [Fact]
    public void Run_MasksASecretThatGotIntoEvidence()
    {
        var checklist = new Checklist([new FakeCheck("SECRET", Severity.High, new Evidence("booking-logic-app", "CARRIER_ENDPOINT_KEY=s3cr3t-value is in its app settings"))]);

        var finding = checklist.Run(Estates.Fixed).Should().ContainSingle().Subject;

        finding.Evidence.Should().ContainSingle().Which.Detail.Should().Be("CARRIER_ENDPOINT_KEY=*** is in its app settings");
        finding.Evidence[0].Subject.Should().Be("booking-logic-app");
    }

    [Fact]
    public void Checks_AreTheOnesTheChecklistWasGiven() =>
        Checklist.Default.Checks.Should().ContainSingle().Which.Should().BeOfType<DeadLetterAlertCheck>();

    private sealed class FakeCheck(string id, Severity severity, Evidence? evidence = null, bool passes = false) : Check
    {
        public override string Id => id;

        public override string Rule => "A rule.";

        public override string Title => "A title.";

        public override Severity Severity => severity;

        public override string AsFoundToggle => "someToggle";

        public override string Fix => "A fix.";

        public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate) => passes ? [] : [evidence ?? new Evidence("thing", "is wrong")];
    }
}
