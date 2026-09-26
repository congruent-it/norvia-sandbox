using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class RateCardTypesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RateCardId_Blank_Throws(string value)
    {
        var act = () => new RateCardId(value);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ZoneName_Blank_Throws(string value)
    {
        var act = () => new ZoneName(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ZoneName_SameName_AreEqual()
    {
        new ZoneName("Rotterdam").Should().Be(new ZoneName("Rotterdam"));
        new ZoneName("Rotterdam").ToString().Should().Be("Rotterdam");
    }
}
