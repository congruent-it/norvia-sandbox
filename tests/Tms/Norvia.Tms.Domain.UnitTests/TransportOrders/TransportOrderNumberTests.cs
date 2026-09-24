using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TransportOrderNumberTests
{
    [Theory]
    [InlineData(1, "TO-0000001")]
    [InlineData(42, "TO-0000042")]
    [InlineData(9_999_999, "TO-9999999")]
    public void FromSequence_ValueInRange_FormatsAsTransportOrderNumber(long sequence, string expected)
    {
        var number = TransportOrderNumber.FromSequence(sequence);

        number.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000)]
    public void FromSequence_ValueOutOfRange_Throws(long sequence)
    {
        var act = () => TransportOrderNumber.FromSequence(sequence);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryParse_WellFormedNumber_ReturnsTheNumber()
    {
        var parsed = TransportOrderNumber.TryParse("TO-0000042", out var number);

        parsed.Should().BeTrue();
        number.Should().Be(TransportOrderNumber.FromSequence(42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TO-42")]
    [InlineData("to-0000042")]
    [InlineData("TO-00000042")]
    [InlineData("SH-0000042")]
    public void TryParse_MalformedNumber_ReturnsFalse(string? value)
    {
        var parsed = TransportOrderNumber.TryParse(value, out var number);

        parsed.Should().BeFalse();
        number.Should().BeNull();
    }

    [Fact]
    public void Parse_WellFormedNumber_ReturnsTheNumber()
    {
        TransportOrderNumber.Parse("TO-0000042").Should().Be(TransportOrderNumber.FromSequence(42));
    }

    [Fact]
    public void Parse_MalformedNumber_Throws()
    {
        var act = () => TransportOrderNumber.Parse("TO-42");

        act.Should().Throw<FormatException>();
    }
}
