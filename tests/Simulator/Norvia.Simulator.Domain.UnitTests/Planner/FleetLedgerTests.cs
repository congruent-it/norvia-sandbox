using Norvia.Simulator.Domain.Planner;

namespace Norvia.Simulator.Domain.UnitTests.Planner;

public sealed class FleetLedgerTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static FleetLedger Ledger(decimal capacityKg = 1000, int capacityPallets = 10, decimal demandKg = 200, int demandPallets = 2) =>
        new([new FleetDay(Today, capacityKg, capacityPallets, demandKg, demandPallets)]);

    [Fact]
    public void TryReserve_WithinTheRoomLeft_TakesIt()
    {
        Ledger().TryReserve(Today, 800, 8).Should().BeTrue();
    }

    [Fact]
    public void TryReserve_TooHeavy_TakesNothing()
    {
        var ledger = Ledger();

        ledger.TryReserve(Today, 801, 1).Should().BeFalse();
        ledger.TryReserve(Today, 800, 8).Should().BeTrue("the failed reservation took nothing");
    }

    [Fact]
    public void TryReserve_TooManyPalletPlaces_TakesNothing()
    {
        Ledger().TryReserve(Today, 10, 9).Should().BeFalse();
    }

    [Fact]
    public void TryReserve_WhatIsReserved_IsNotThereForTheNextOrder()
    {
        var ledger = Ledger();
        ledger.TryReserve(Today, 500, 4).Should().BeTrue();

        ledger.TryReserve(Today, 400, 1).Should().BeFalse();
    }

    [Fact]
    public void TryReserve_ADayTheTmsDidNotReport_HasNoRoom()
    {
        Ledger().TryReserve(Today.AddDays(30), 1, 0).Should().BeFalse();
    }

    [Fact]
    public void TryReserve_AnOverbookedDay_HasNoRoom()
    {
        Ledger(capacityKg: 100, demandKg: 500).TryReserve(Today, 1, 0).Should().BeFalse();
    }

    [Fact]
    public void Release_GivesTheRoomBack()
    {
        var ledger = Ledger();
        ledger.TryReserve(Today, 800, 8).Should().BeTrue();

        ledger.Release(Today, 800, 8);

        ledger.TryReserve(Today, 800, 8).Should().BeTrue();
    }

    [Fact]
    public void Release_ADayTheTmsDidNotReport_ChangesNothing()
    {
        var act = () => Ledger().Release(Today.AddDays(30), 1, 0);

        act.Should().NotThrow();
    }
}
