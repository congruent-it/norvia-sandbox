using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.Carriers;

public sealed class CarrierTests
{
    private static readonly CarrierId Id = new("CAR-0001");

    [Fact]
    public void Create_WithAnIdAndAName_KeepsThem()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        carrier.Id.Should().Be(Id);
        carrier.Name.Should().Be("Van Dijk Transport");
        carrier.Active.Should().BeTrue();
    }

    [Fact]
    public void Create_WithoutAName_Throws()
    {
        var creating = () => new Carrier(Id, "", active: true);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reload_TakesOverWhatTheSeedFileSaysNow()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        carrier.Reload("Van Dijk BV", active: false);

        carrier.Name.Should().Be("Van Dijk BV");
        carrier.Active.Should().BeFalse();
    }

    [Fact]
    public void Reload_WithoutAName_Throws()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        var reloading = () => carrier.Reload(" ", active: true);

        reloading.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_StartsAtRevisionOne() => new Carrier(Id, "Van Dijk Transport", active: true).Revision.Should().Be(1);

    [Fact]
    public void Edit_ReplacesTheFieldsAndRaisesTheRevision()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        carrier.Edit("Van Dijk BV", active: false);

        carrier.Name.Should().Be("Van Dijk BV");
        carrier.Active.Should().BeFalse();
        carrier.Revision.Should().Be(2);
    }

    [Fact]
    public void Edit_WithDifferentContent_SaysItChangedTheRecord()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        carrier.Edit("Van Dijk Transport BV", active: true).Should().BeTrue();
    }

    [Fact]
    public void Edit_WithTheContentItAlreadyHas_SaysNothingChangedAndKeepsTheRevisionAndTheSeedFileInCharge()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        var changed = carrier.Edit("Van Dijk Transport", active: true);

        changed.Should().BeFalse();
        carrier.Revision.Should().Be(1);
        carrier.FinanceOwned.Should().BeFalse();
    }

    [Fact]
    public void Edit_WithoutAName_ThrowsAndChangesNothing()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        var editing = () => carrier.Edit(" ", active: false);

        editing.Should().Throw<ArgumentException>();
        carrier.Revision.Should().Be(1);
        carrier.Active.Should().BeTrue();
    }

    [Fact]
    public void Reload_AfterAnEdit_LeavesTheEditAlone()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);
        carrier.Edit("Van Dijk BV", active: true);

        carrier.Reload("Seed name", active: false);

        carrier.Name.Should().Be("Van Dijk BV");
        carrier.Revision.Should().Be(2);
    }

    [Fact]
    public void Reload_OfACarrierFinanceCreated_LeavesItAlone()
    {
        var carrier = Carrier.CreateByFinance(Id, "Nieuw", active: true);

        carrier.Reload("Seed name", active: false);

        carrier.Name.Should().Be("Nieuw");
        carrier.Revision.Should().Be(1);
    }

    [Fact]
    public void Reload_BeforeAnyEdit_KeepsRevisionOne()
    {
        var carrier = new Carrier(Id, "Van Dijk Transport", active: true);

        carrier.Reload("Van Dijk BV", active: true);

        carrier.Revision.Should().Be(1);
    }

    [Fact]
    public void CarrierId_WithoutAValue_Throws()
    {
        var creating = () => new CarrierId("");

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CarrierId_ToString_IsTheValue() => Id.ToString().Should().Be("CAR-0001");

    [Fact]
    public void CarriersSpec_ListsThemByName()
    {
        var carriers = new[] { new Carrier(new CarrierId("CAR-2"), "Zeta", true), new Carrier(new CarrierId("CAR-1"), "Alpha", true) };

        new CarriersSpec().Evaluate(carriers).Select(carrier => carrier.Name).Should().Equal("Alpha", "Zeta");
    }

    [Fact]
    public void CarrierByIdSpec_FindsOnlyThatCarrier()
    {
        var carriers = new[] { new Carrier(new CarrierId("CAR-2"), "Zeta", true), new Carrier(Id, "Alpha", true) };

        new CarrierByIdSpec(Id).Evaluate(carriers).Should().ContainSingle().Which.Name.Should().Be("Alpha");
    }
}
