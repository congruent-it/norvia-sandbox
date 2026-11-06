using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Domain.UnitTests.Personas;

public sealed class PoolTextRulesTests
{
    [Theory]
    [InlineData("Geen wagen vrij")]
    [InlineData("Regrettably, our fleet is fully committed on the requested date")]
    [InlineData("sorry geen capacity die dag!!")]
    [InlineData("Leider keine freie Kapazität an diesem Tag")]
    [InlineData("Truck 2 is stuk, back in 3 days")]
    public void IsAcceptable_ARealisticText_Passes(string text) => PoolTextRules.IsAcceptable(text).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Line one\nline two")]
    [InlineData("tab\there")]
    [InlineData("bell\u0007")]
    [InlineData("see https://example.com/track")]
    [InlineData("see www.example.com")]
    [InlineData("go to carrier.nl for info")]
    [InlineData("mail dispatch@carrier.nl")]
    [InlineData("bel 06 1234 5678")]
    [InlineData("call +31 10 123 4567")]
    [InlineData("tel 0049-211-123456")]
    [InlineData("at 3011 AD Rotterdam")]
    [InlineData("at 3011AD")]
    [InlineData("naar 40213 Düsseldorf")]
    [InlineData("Hoofdstraat 12 was dicht")]
    [InlineData("Baker Street 221 closed")]
    [InlineData("12 Baker Road closed")]
    [InlineData("Hauptstrasse 5 geschlossen")]
    [InlineData("vul de postcode in")]
    public void IsAcceptable_ABadText_Fails(string? text) => PoolTextRules.IsAcceptable(text).Should().BeFalse();

    [Fact]
    public void IsAcceptable_ATextOverTheCap_Fails()
    {
        PoolTextRules.IsAcceptable(new string('a', PoolTextRules.MaxLength)).Should().BeTrue();
        PoolTextRules.IsAcceptable(new string('a', PoolTextRules.MaxLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void Acceptable_DropsTheBadOnesTrimsAndKeepsEachOnceInOrder()
    {
        var texts = PoolTextRules.Acceptable(["  Vol ", "https://x.nl", "Geen wagen", "Vol", null, "Dock dicht"]);

        texts.Should().Equal("Vol", "Geen wagen", "Dock dicht");
    }
}
