using Norvia.Erp.Domain.Drivers;

namespace Norvia.Erp.Domain.UnitTests.Drivers;

public sealed class DriverTests
{
    private static readonly DriverId Id = new("DRV-0001");

    private static Driver Anouk() => new(Id, "Anouk de Vries", [LicenceCategory.B, LicenceCategory.C], active: true);

    [Fact]
    public void Create_KeepsWhatItIsGiven_AtRevisionOne()
    {
        var driver = Anouk();

        driver.Name.Should().Be("Anouk de Vries");
        driver.Licences.Should().BeEquivalentTo([LicenceCategory.B, LicenceCategory.C]);
        driver.Active.Should().BeTrue();
        driver.Revision.Should().Be(1);
        driver.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void Create_WithoutAName_Throws() =>
        FluentActions.Invoking(() => new Driver(Id, "", [], true)).Should().Throw<ArgumentException>();

    [Fact]
    public void Create_KeepsEachLicenceOnce() =>
        new Driver(Id, "A", [LicenceCategory.B, LicenceCategory.B], true).Licences.Should().Equal(LicenceCategory.B);

    [Fact]
    public void Edit_ReplacesTheFieldsRaisesTheRevisionAndTakesTheRecordFromTheSeedFile()
    {
        var driver = Anouk();

        driver.Edit("Anouk Bakker", [LicenceCategory.CE], active: false).Should().BeTrue();

        driver.Name.Should().Be("Anouk Bakker");
        driver.Licences.Should().Equal(LicenceCategory.CE);
        driver.Active.Should().BeFalse();
        driver.Revision.Should().Be(2);
        driver.FinanceOwned.Should().BeTrue();
    }

    [Fact]
    public void Edit_WithTheSameLicencesInAnotherOrder_SaysNothingChanged()
    {
        var driver = Anouk();

        driver.Edit("Anouk de Vries", [LicenceCategory.C, LicenceCategory.B], active: true).Should().BeFalse();

        driver.Revision.Should().Be(1);
        driver.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void Edit_MakingItInactive_ChangesOnlyTheActiveFlag()
    {
        var driver = Anouk();

        driver.Edit("Anouk de Vries", [LicenceCategory.B, LicenceCategory.C], active: false).Should().BeTrue();

        driver.Name.Should().Be("Anouk de Vries");
        driver.Licences.Should().HaveCount(2);
        driver.Active.Should().BeFalse();
    }

    [Fact]
    public void Edit_WithoutAName_ThrowsAndChangesNothing()
    {
        var driver = Anouk();

        FluentActions.Invoking(() => driver.Edit(" ", [], false)).Should().Throw<ArgumentException>();

        driver.Revision.Should().Be(1);
    }

    [Fact]
    public void Reload_BeforeAnyEdit_TakesOverTheSeedFileAndKeepsRevisionOne()
    {
        var driver = Anouk();

        driver.Reload("Anouk B", [LicenceCategory.CE], active: false);

        driver.Name.Should().Be("Anouk B");
        driver.Licences.Should().Equal(LicenceCategory.CE);
        driver.Revision.Should().Be(1);
    }

    [Fact]
    public void Reload_AfterAnEdit_LeavesTheEditAlone()
    {
        var driver = Anouk();
        driver.Edit("Edited", [], true);

        driver.Reload("Seed", [LicenceCategory.B], false);

        driver.Name.Should().Be("Edited");
    }

    [Fact]
    public void Reload_OfADriverFinanceCreated_LeavesItAlone()
    {
        var driver = Driver.CreateByFinance(Id, "New", [LicenceCategory.B], true);

        driver.Reload("Seed", [], false);

        driver.Name.Should().Be("New");
    }

    [Fact]
    public void Reload_WithoutAName_Throws() =>
        FluentActions.Invoking(() => Anouk().Reload("", [], true)).Should().Throw<ArgumentException>();

    [Fact]
    public void DriverId_WithoutAValue_Throws() => FluentActions.Invoking(() => new DriverId(" ")).Should().Throw<ArgumentException>();

    [Fact]
    public void DriverId_ToString_IsTheValue() => Id.ToString().Should().Be("DRV-0001");

    [Fact]
    public void DriverByIdSpec_FindsOnlyThatDriver()
    {
        var drivers = new[] { new Driver(new DriverId("D2"), "Zeta", [], true), Anouk() };

        new DriverByIdSpec(Id).Evaluate(drivers).Should().ContainSingle().Which.Name.Should().Be("Anouk de Vries");
    }
}
