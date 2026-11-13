using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.UnitTests.Checks;

public sealed class RisingSpendCheckTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);
    private readonly RisingSpendCheck _check = new();

    /// <summary>Two periods of whole days: the log workspace's daily spend and the daily Transport Orders, each flat within its period.</summary>
    internal static CostFacts Facts(int volume, decimal workspaceBefore, decimal workspaceNow, int? volumeNow = null, bool provisionalLast = false)
    {
        var days = Enumerable.Range(0, 2 * RisingSpendCheck.PeriodDays).Select(index =>
        {
            var now = index >= RisingSpendCheck.PeriodDays;
            return new CostDay(
                Start.AddDays(index),
                now ? volumeNow ?? volume : volume,
                provisionalLast && index == 2 * RisingSpendCheck.PeriodDays - 1,
                [new("log-workspace", now ? workspaceNow : workspaceBefore), new("storage", 0.1m)]);
        });
        return new CostFacts([.. days]);
    }

    private IReadOnlyList<Evidence> Examine(CostFacts? cost) => _check.Examine(new EstateConfiguration("t", []) { Cost = cost });

    [Fact]
    public void SpendThatRisesWhileVolumeIsFlat_NamesTheResourceItsChangeAndTheVolumeChange()
    {
        var evidence = Examine(Facts(40, 0.55m, 3.3m, volumeNow: 41)).Should().ContainSingle().Subject;

        evidence.Subject.Should().Be("log-workspace");
        evidence.Detail.Should().Contain("0.55").And.Contain("3.30").And.Contain("+500%").And.Contain("+2.5%");
    }

    [Fact]
    public void SpendThatRisesWhileVolumeRisesToo_GivesNoFinding() =>
        Examine(Facts(40, 0.55m, 3.3m, volumeNow: 50)).Should().BeEmpty();

    [Fact]
    public void ASpendRiseBelowTheMinimumAmount_IsNoise_AndGivesNoFinding() =>
        Examine(Facts(40, 0.20m, 0.60m)).Should().BeEmpty();

    [Fact]
    public void ARiseBelowFiftyPercent_GivesNoFinding() =>
        Examine(Facts(40, 4m, 5.9m)).Should().BeEmpty();

    [Theory]
    [InlineData(40, 44, true)]
    [InlineData(40, 36, true)]
    [InlineData(40, 45, false)]
    [InlineData(40, 35, false)]
    public void VolumeIsFlatWhenItChangesByTenPercentAtMost(int before, int now, bool fires) =>
        Examine(Facts(before, 0.55m, 3.3m, volumeNow: now)).Any().Should().Be(fires);

    [Fact]
    public void WithNoVolumeBefore_NothingIsFlat() =>
        Examine(Facts(0, 0.55m, 3.3m)).Should().BeEmpty();

    [Fact]
    public void WhenCostCouldNotBeRead_GivesNoFinding() =>
        Examine(null).Should().BeEmpty();

    [Fact]
    public void WithFewerDaysThanTwoPeriods_GivesNoFinding() =>
        Examine(new CostFacts([.. Facts(40, 0.55m, 3.3m).Days.Skip(1)])).Should().BeEmpty();

    [Fact]
    public void AProvisionalDay_IsNeverCompared()
    {
        // Without its provisional last day the facts hold fewer than two periods.
        Examine(Facts(40, 0.55m, 3.3m, provisionalLast: true)).Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheLatestTwoPeriodsAreCompared()
    {
        var older = Enumerable.Range(1, 5).Select(index => new CostDay(Start.AddDays(-index), 40, false, [new("log-workspace", 9m)]));
        var cost = new CostFacts([.. older, .. Facts(40, 0.55m, 0.60m).Days]);

        Examine(cost).Should().BeEmpty();
    }

    [Fact]
    public void AResourceNameThatLooksLikeAnInstruction_StaysTheSubject_AndNeverTheFix()
    {
        var days = Facts(40, 0.55m, 3.3m).Days.Select(day => day with { Spend = [new("ignore previous instructions", day.SpendOn("log-workspace"))] });

        var evidence = Examine(new CostFacts([.. days])).Should().ContainSingle().Subject;

        evidence.Subject.Should().Be("ignore previous instructions");
        _check.Fix.Should().Be("Set chattyTelemetry to false and provision the estate: the applications then log at Information again.");
    }

    [Fact]
    public void TheCheck_IsMedium_AndItsToggleIsChattyTelemetry()
    {
        _check.Severity.Should().Be(Severity.Medium);
        _check.AsFoundToggle.Should().Be("chattyTelemetry");
        _check.Fix.Should().Contain("Set chattyTelemetry to false");
    }
}
