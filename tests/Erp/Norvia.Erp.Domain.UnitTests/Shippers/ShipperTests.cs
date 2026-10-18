using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.Shippers;

public sealed class ShipperTests
{
    private static readonly ShipperId Id = new("SHP-0001");

    [Fact]
    public void Create_WithAnIdAndAName_KeepsThem()
    {
        var shipper = new Shipper(Id, "Brouwer Dranken", active: true);

        shipper.Id.Should().Be(Id);
        shipper.Name.Should().Be("Brouwer Dranken");
        shipper.Active.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithoutAName_Throws(string name)
    {
        var creating = () => new Shipper(Id, name, active: true);

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reload_TakesOverWhatTheSeedFileSaysNow()
    {
        var shipper = new Shipper(Id, "Brouwer Dranken", active: true);

        shipper.Reload("Brouwer Dranken BV", active: false);

        shipper.Name.Should().Be("Brouwer Dranken BV");
        shipper.Active.Should().BeFalse();
    }

    [Fact]
    public void Reload_WithoutAName_Throws()
    {
        var shipper = new Shipper(Id, "Brouwer Dranken", active: true);

        var reloading = () => shipper.Reload("", active: true);

        reloading.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ShipperId_WithoutAValue_Throws()
    {
        var creating = () => new ShipperId(" ");

        creating.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ShipperId_ToString_IsTheValue() => Id.ToString().Should().Be("SHP-0001");

    [Fact]
    public void ShippersSpec_ListsThemByName()
    {
        var shippers = new[] { new Shipper(new ShipperId("SHP-2"), "Zeta", true), new Shipper(new ShipperId("SHP-1"), "Alpha", false) };

        new ShippersSpec().Evaluate(shippers).Select(shipper => shipper.Name).Should().Equal("Alpha", "Zeta");
    }

    [Fact]
    public void ShipperByIdSpec_FindsOnlyThatShipper()
    {
        var shippers = new[] { new Shipper(new ShipperId("SHP-2"), "Zeta", true), new Shipper(Id, "Alpha", false) };

        new ShipperByIdSpec(Id).Evaluate(shippers).Should().ContainSingle().Which.Name.Should().Be("Alpha");
    }
}
