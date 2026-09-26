using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class IdempotencyKeyTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void New_Blank_IsRefused(string value)
    {
        var act = () => new IdempotencyKey(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void New_LongerThanTheMaximum_IsRefused()
    {
        var act = () => new IdempotencyKey(new string('k', IdempotencyKey.MaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void New_AtTheMaximum_KeepsTheValue()
    {
        var value = new string('k', IdempotencyKey.MaxLength);

        new IdempotencyKey(value).Value.Should().Be(value);
    }
}
