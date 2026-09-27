using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

public sealed class ShipmentNumberTests
{
    [Theory]
    [InlineData(1, "SH-0000001")]
    [InlineData(42, "SH-0000042")]
    [InlineData(9_999_999, "SH-9999999")]
    public void FromSequence_ValueInRange_FormatsAsShipmentNumber(long sequence, string expected)
    {
        var number = ShipmentNumber.FromSequence(sequence);

        number.Value.Should().Be(expected);
        number.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000)]
    public void FromSequence_ValueOutOfRange_Throws(long sequence)
    {
        var act = () => ShipmentNumber.FromSequence(sequence);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryParse_WellFormedNumber_ReturnsTheNumber()
    {
        var parsed = ShipmentNumber.TryParse("SH-0000042", out var number);

        parsed.Should().BeTrue();
        number.Should().Be(ShipmentNumber.FromSequence(42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TO-0000042")]
    [InlineData("SH-42")]
    public void TryParse_MalformedNumber_ReturnsFalse(string? value)
    {
        var parsed = ShipmentNumber.TryParse(value, out var number);

        parsed.Should().BeFalse();
        number.Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedNumber_Throws()
    {
        var act = () => ShipmentNumber.Parse("SH-42");

        act.Should().Throw<FormatException>();
    }
}
