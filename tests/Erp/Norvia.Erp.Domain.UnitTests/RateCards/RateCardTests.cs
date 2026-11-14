using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.RateCards;

public sealed class RateCardTests
{
    private static readonly RateCardVersion VersionOne = Version(1);

    [Fact]
    public void CreateRateCard_KeepsItsShipperAndVersions()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        card.Id.Value.Should().Be("RC-0001");
        card.ShipperId.Value.Should().Be("SHP-0001");
        card.Versions.Should().ContainSingle().Which.Should().Be(VersionOne);
    }

    [Fact]
    public void ReloadRateCard_TakesOverWhatTheSeedFileSaysNow()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        card.Reload(new ShipperId("SHP-0002"), [VersionOne, Version(2)]);

        card.ShipperId.Value.Should().Be("SHP-0002");
        card.Versions.Select(version => version.Version).Should().Equal(1, 2);
    }

    [Fact]
    public void CreateRateCard_WithoutVersions_Throws()
    {
        var creating = () => new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), null!);

        creating.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReloadRateCard_WithoutVersions_Throws()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        var reloading = () => card.Reload(new ShipperId("SHP-0001"), null!);

        reloading.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateCarrierRateCard_KeepsItsCarrierAndVersions()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);

        card.Id.Value.Should().Be("CRC-0001");
        card.CarrierId.Value.Should().Be("CAR-0001");
        card.Versions.Should().ContainSingle();
    }

    [Fact]
    public void ReloadCarrierRateCard_TakesOverWhatTheSeedFileSaysNow()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);

        card.Reload(new CarrierId("CAR-0002"), [Version(2)]);

        card.CarrierId.Value.Should().Be("CAR-0002");
        card.Versions.Should().ContainSingle().Which.Version.Should().Be(2);
    }

    [Fact]
    public void CreateCarrierRateCard_WithoutVersions_Throws()
    {
        var creating = () => new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), null!);

        creating.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReloadCarrierRateCard_WithoutVersions_Throws()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);

        var reloading = () => card.Reload(new CarrierId("CAR-0001"), null!);

        reloading.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateRateCard_StartsAtRevisionOne() =>
        new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]).Revision.Should().Be(1);

    [Fact]
    public void EditRateCard_ReplacesTheWholeListAndRaisesTheRevision()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);
        var closed = VersionOne with { ValidTo = new DateOnly(2026, 6, 30) };

        var changed = card.Edit(new ShipperId("SHP-0001"), [closed, Version(2, new DateOnly(2026, 7, 1))]);

        changed.Should().BeTrue();
        card.Versions.Select(version => version.Version).Should().Equal(1, 2);
        card.Revision.Should().Be(2);
        card.FinanceOwned.Should().BeTrue();
    }

    [Fact]
    public void EditRateCard_WithTheSameVersions_SaysNothingChangedAndKeepsTheRevisionAndTheSeedFileInCharge()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        var changed = card.Edit(new ShipperId("SHP-0001"), [Version(1)]);

        changed.Should().BeFalse();
        card.Revision.Should().Be(1);
        card.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void EditRateCard_WithAChangedPrice_SaysItChanged()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);
        var dearer = VersionOne with { Lanes = [new Lane("Rotterdam", "Rotterdam", 55m, [new WeightBreak(500, 9.6m)])] };

        card.Edit(new ShipperId("SHP-0001"), [dearer]).Should().BeTrue();
    }

    [Fact]
    public void EditRateCard_WithAnotherShipper_SaysItChanged()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        card.Edit(new ShipperId("SHP-0002"), [VersionOne]).Should().BeTrue();

        card.ShipperId.Value.Should().Be("SHP-0002");
    }

    [Fact]
    public void EditRateCard_WithVersionsThatOverlap_ThrowsAndChangesNothing()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        var editing = () => card.Edit(new ShipperId("SHP-0001"), [VersionOne, Version(2, new DateOnly(2026, 3, 1))]);

        editing.Should().Throw<ArgumentException>().WithMessage("*overlap*");
        card.Revision.Should().Be(1);
        card.Versions.Should().ContainSingle();
    }

    [Fact]
    public void EditRateCard_WithTheSameVersionNumberTwice_Throws()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);
        var closed = VersionOne with { ValidTo = new DateOnly(2026, 6, 30) };

        var editing = () => card.Edit(new ShipperId("SHP-0001"), [closed, Version(1, new DateOnly(2026, 7, 1))]);

        editing.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EditRateCard_WithAVersionThatEndsBeforeItStarts_Throws()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);
        var backwards = VersionOne with { ValidTo = new DateOnly(2025, 12, 31) };

        var editing = () => card.Edit(new ShipperId("SHP-0001"), [backwards]);

        editing.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EditRateCard_WithoutVersions_Throws()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);

        var editing = () => card.Edit(new ShipperId("SHP-0001"), []);

        editing.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EditRateCard_WithVersionsInAnyOrder_ComparesThemByVersionNumber()
    {
        var closed = VersionOne with { ValidTo = new DateOnly(2026, 6, 30) };
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [closed, Version(2, new DateOnly(2026, 7, 1))]);

        card.Edit(new ShipperId("SHP-0001"), [Version(2, new DateOnly(2026, 7, 1)), closed]).Should().BeFalse();
    }

    [Fact]
    public void ReloadRateCard_AfterAnEdit_LeavesTheEditAlone()
    {
        var card = new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), [VersionOne]);
        card.Edit(new ShipperId("SHP-0001"), [VersionOne with { ValidTo = new DateOnly(2026, 6, 30) }]);

        card.Reload(new ShipperId("SHP-0002"), [Version(7)]);

        card.ShipperId.Value.Should().Be("SHP-0001");
        card.Versions.Single().ValidTo.Should().Be(new DateOnly(2026, 6, 30));
        card.Revision.Should().Be(2);
    }

    [Fact]
    public void ReloadRateCard_OfACardFinanceCreated_LeavesItAlone()
    {
        var card = RateCard.CreateByFinance(new RateCardId("RC-0009"), new ShipperId("SHP-0001"), [VersionOne]);

        card.Reload(new ShipperId("SHP-0002"), [Version(7)]);

        card.ShipperId.Value.Should().Be("SHP-0001");
        card.Revision.Should().Be(1);
    }

    [Fact]
    public void CreateRateCardByFinance_WithVersionsThatOverlap_Throws()
    {
        var creating = () => RateCard.CreateByFinance(new RateCardId("RC-0009"), new ShipperId("SHP-0001"), [VersionOne, Version(2)]);

        creating.Should().Throw<ArgumentException>().WithMessage("*overlap*");
    }

    [Fact]
    public void CreateCarrierRateCard_StartsAtRevisionOne() =>
        new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]).Revision.Should().Be(1);

    [Fact]
    public void EditCarrierRateCard_ReplacesTheWholeListAndRaisesTheRevision()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);
        var closed = VersionOne with { ValidTo = new DateOnly(2026, 6, 30) };

        var changed = card.Edit(new CarrierId("CAR-0001"), [closed, Version(2, new DateOnly(2026, 7, 1))]);

        changed.Should().BeTrue();
        card.Versions.Select(version => version.Version).Should().Equal(1, 2);
        card.Revision.Should().Be(2);
        card.FinanceOwned.Should().BeTrue();
    }

    [Fact]
    public void EditCarrierRateCard_WithTheSameVersions_SaysNothingChangedAndKeepsTheRevisionAndTheSeedFileInCharge()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);

        var changed = card.Edit(new CarrierId("CAR-0001"), [Version(1)]);

        changed.Should().BeFalse();
        card.Revision.Should().Be(1);
        card.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void EditCarrierRateCard_WithVersionsThatOverlap_ThrowsAndChangesNothing()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);

        var editing = () => card.Edit(new CarrierId("CAR-0001"), [VersionOne, Version(2, new DateOnly(2026, 3, 1))]);

        editing.Should().Throw<ArgumentException>().WithMessage("*overlap*");
        card.Revision.Should().Be(1);
    }

    [Fact]
    public void ReloadCarrierRateCard_AfterAnEdit_LeavesTheEditAlone()
    {
        var card = new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), [VersionOne]);
        card.Edit(new CarrierId("CAR-0001"), [VersionOne with { ValidTo = new DateOnly(2026, 6, 30) }]);

        card.Reload(new CarrierId("CAR-0002"), [Version(7)]);

        card.CarrierId.Value.Should().Be("CAR-0001");
        card.Revision.Should().Be(2);
    }

    [Fact]
    public void ReloadCarrierRateCard_OfACardFinanceCreated_LeavesItAlone()
    {
        var card = CarrierRateCard.CreateByFinance(new RateCardId("CRC-0009"), new CarrierId("CAR-0001"), [VersionOne]);

        card.Reload(new CarrierId("CAR-0002"), [Version(7)]);

        card.CarrierId.Value.Should().Be("CAR-0001");
    }

    [Fact]
    public void RateCardId_WithoutAValue_Throws()
    {
        var creating = () => new RateCardId("");

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RateCardId_ToString_IsTheValue() => new RateCardId("RC-0001").ToString().Should().Be("RC-0001");

    [Fact]
    public void RateCardsSpec_ListsEveryCard()
    {
        var cards = new[]
        {
            new RateCard(new RateCardId("RC-0002"), new ShipperId("SHP-0003"), []),
            new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), []),
        };

        new RateCardsSpec().Evaluate(cards).Select(card => card.Id.Value).Should().BeEquivalentTo(["RC-0001", "RC-0002"]);
    }

    [Fact]
    public void RateCardByIdSpec_FindsOnlyThatCard()
    {
        var cards = new[]
        {
            new RateCard(new RateCardId("RC-0002"), new ShipperId("SHP-0003"), []),
            new RateCard(new RateCardId("RC-0001"), new ShipperId("SHP-0001"), []),
        };

        new RateCardByIdSpec(new RateCardId("RC-0001")).Evaluate(cards).Should().ContainSingle().Which.ShipperId.Value.Should().Be("SHP-0001");
    }

    [Fact]
    public void CarrierRateCardsSpec_ListsEveryCard()
    {
        var cards = new[]
        {
            new CarrierRateCard(new RateCardId("CRC-0002"), new CarrierId("CAR-0002"), []),
            new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), []),
        };

        new CarrierRateCardsSpec().Evaluate(cards).Select(card => card.Id.Value).Should().BeEquivalentTo(["CRC-0001", "CRC-0002"]);
    }

    [Fact]
    public void CarrierRateCardByIdSpec_FindsOnlyThatCard()
    {
        var cards = new[]
        {
            new CarrierRateCard(new RateCardId("CRC-0002"), new CarrierId("CAR-0002"), []),
            new CarrierRateCard(new RateCardId("CRC-0001"), new CarrierId("CAR-0001"), []),
        };

        new CarrierRateCardByIdSpec(new RateCardId("CRC-0002")).Evaluate(cards).Should().ContainSingle().Which.CarrierId.Value.Should().Be("CAR-0002");
    }

    private static RateCardVersion Version(int number, DateOnly? validFrom = null) =>
        new(number, validFrom ?? new DateOnly(2026, 1, 1), null, [new Zone("Rotterdam", "NL", ["30"])], [new Lane("Rotterdam", "Rotterdam", 55m, [new WeightBreak(500, 9.5m)])]);
}
