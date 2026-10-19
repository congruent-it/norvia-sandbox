using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.UnitTests.Runs;

public sealed class SimulationSettingsTests
{
    private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly SimulationLimits Limits = new(MaxOrdersPerRun: 100, MaxConcurrentRequests: 4);

    private static SimulationSettings Settings(int orders = 10, int bad = 10, int today = 30, RunMode mode = RunMode.Burst, int windowMinutes = 0) =>
        new(orders, bad, today, 1, mode, TimeSpan.FromMinutes(windowMinutes));

    [Fact]
    public void Problems_SettingsWithinTheLimits_AreNone()
    {
        Settings().Problems(Limits).Should().BeEmpty();
    }

    [Fact]
    public void Problems_DriverSharesOutOfRange_AreNamed()
    {
        (Settings() with { Drivers = new DriverShares(CollectionFailedPercent: 101) }).Problems(Limits).Should().ContainSingle().Which.Should().Contain("collection fails");
    }

    [Fact]
    public void Drivers_ASettingsDocumentFromBeforeTheDriverStage_ReadsWithTheDefaultShares()
    {
        var older = JsonSerializer.Deserialize<SimulationSettings>(
            """{"orderCount":10,"badSharePercent":10,"collectedTodayPercent":30,"seed":1,"mode":"Burst","window":"00:00:00"}""", PlanJson);

        older!.Drivers.Should().Be(new DriverShares());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Problems_ATripShareOutOfRange_IsNamed(int percent)
    {
        (Settings() with { TripPercent = percent }).Problems(Limits).Should().ContainSingle().Which.Should().Contain("on Trips").And.Contain("0 to 100");
    }

    [Fact]
    public void TripPercent_ASettingsDocumentFromBeforeTheTrips_ReadsWithAllOfThemOnTrips()
    {
        var older = JsonSerializer.Deserialize<SimulationSettings>(
            """{"orderCount":10,"badSharePercent":10,"collectedTodayPercent":30,"seed":1,"mode":"Burst","window":"00:00:00"}""", PlanJson);

        older!.TripPercent.Should().Be(100);
    }

    [Fact]
    public void Problems_MoreOrdersThanTheCap_RefusesThePlan()
    {
        Settings(orders: 101).Problems(Limits).Should().ContainSingle().Which.Should().Contain("at most 100");
    }

    [Theory]
    [InlineData(-1, 50, 5, "assigns")]
    [InlineData(101, 50, 5, "assigns")]
    [InlineData(80, -1, 5, "Own fleet")]
    [InlineData(80, 101, 5, "Own fleet")]
    [InlineData(80, 50, -1, "reassigns")]
    [InlineData(80, 50, 101, "reassigns")]
    public void Problems_APlannerShareOutOfRange_IsNamed(int assigned, int ownFleet, int reassign, string named)
    {
        var settings = new SimulationSettings(10, 10, 30, 1, RunMode.Burst, TimeSpan.Zero, null, assigned, ownFleet, reassign);

        settings.Problems(Limits).Should().ContainSingle().Which.Should().Contain(named).And.Contain("0 to 100");
    }

    [Fact]
    public void Problems_CarrierSharesOutOfRange_AreNamed()
    {
        var settings = Settings() with { Carriers = new CarrierShares(ReleasePercent: 101, CancelPercent: -1) };

        settings.Problems(Limits).Should().HaveCount(2).And.Contain(problem => problem.Contains("releases")).And.Contain(problem => problem.Contains("cancels"));
    }

    [Fact]
    public void Problems_MoreConfirmedAndDeclinedThanReleased_IsRefused()
    {
        var settings = Settings() with { Carriers = new CarrierShares(ConfirmPercent: 70, DeclinePercent: 40) };

        settings.Problems(Limits).Should().ContainSingle().Which.Should().Contain("confirm and decline more than all");
    }

    [Fact]
    public void Carriers_ASettingsDocumentFromBeforeTheCarrierStage_ReadsWithTheDefaultShares()
    {
        var older = JsonSerializer.Deserialize<SimulationSettings>(
            """{"orderCount":10,"badSharePercent":10,"collectedTodayPercent":30,"seed":1,"mode":"Burst","window":"00:00:00"}""", PlanJson);

        older!.Carriers.Should().Be(new CarrierShares());
    }

    [Fact]
    public void Problems_InvoicingSharesOutOfRange_AreNamed()
    {
        var settings = Settings() with { Invoicing = new InvoicingShares(101, -1) };

        settings.Problems(Limits).Should().HaveCount(2).And.Contain(problem => problem.Contains("invoices")).And.Contain(problem => problem.Contains("credits"));
    }

    [Fact]
    public void Invoicing_ASettingsDocumentFromBeforeInvoicing_ReadsWithTheDefaultShares()
    {
        var older = JsonSerializer.Deserialize<SimulationSettings>(
            """{"orderCount":10,"badSharePercent":10,"collectedTodayPercent":30,"seed":1,"mode":"Burst","window":"00:00:00"}""", PlanJson);

        older!.Invoicing.Should().Be(new InvoicingShares());
    }

    [Fact]
    public void Problems_AnOrderCountOfTheCap_IsAllowed()
    {
        Settings(orders: 100).Problems(Limits).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Problems_NoOrders_AreRefused(int orders)
    {
        Settings(orders: orders).Problems(Limits).Should().ContainSingle().Which.Should().Contain("at least one");
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(101, 30)]
    [InlineData(10, -1)]
    [InlineData(10, 101)]
    public void Problems_ASharePastZeroToOneHundred_IsRefused(int bad, int today)
    {
        Settings(bad: bad, today: today).Problems(Limits).Should().ContainSingle().Which.Should().Contain("percentage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void Problems_APacedRunWithoutAUsableWindow_IsRefused(int minutes)
    {
        Settings(mode: RunMode.Paced, windowMinutes: minutes).Problems(Limits).Should().ContainSingle().Which.Should().Contain("minutes");
    }

    [Fact]
    public void Problems_ABurstRun_NeedsNoWindow()
    {
        Settings(mode: RunMode.Burst, windowMinutes: 0).Problems(Limits).Should().BeEmpty();
    }

    [Fact]
    public void Presets_EachFillsTheSettingsWithTheChosenSeed()
    {
        foreach (var preset in Presets.All)
        {
            var settings = preset.ToSettings(99);
            settings.Seed.Should().Be(99);
            settings.PresetName.Should().Be(preset.Name);
            settings.OrderCount.Should().Be(preset.OrderCount);
        }

        Presets.Find("Quiet Monday").Should().NotBeNull();
        Presets.Find("nope").Should().BeNull();
    }
}
