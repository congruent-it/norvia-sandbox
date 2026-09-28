using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingNumberTests
{
    [Theory]
    [InlineData(1, "CB-0000001")]
    [InlineData(42, "CB-0000042")]
    [InlineData(9_999_999, "CB-9999999")]
    public void FromSequence_ValueInRange_FormatsAsCarrierBookingNumber(long sequence, string expected)
    {
        var number = CarrierBookingNumber.FromSequence(sequence);

        number.Value.Should().Be(expected);
        number.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000)]
    public void FromSequence_ValueOutOfRange_Throws(long sequence)
    {
        var act = () => CarrierBookingNumber.FromSequence(sequence);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryParse_WellFormedNumber_ReturnsTheNumber()
    {
        var parsed = CarrierBookingNumber.TryParse("CB-0000042", out var number);

        parsed.Should().BeTrue();
        number.Should().Be(CarrierBookingNumber.FromSequence(42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SH-0000042")]
    [InlineData("CB-42")]
    public void TryParse_MalformedNumber_ReturnsFalse(string? value)
    {
        var parsed = CarrierBookingNumber.TryParse(value, out var number);

        parsed.Should().BeFalse();
        number.Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedNumber_Throws()
    {
        var act = () => CarrierBookingNumber.Parse("CB-42");

        act.Should().Throw<FormatException>();
    }
}
