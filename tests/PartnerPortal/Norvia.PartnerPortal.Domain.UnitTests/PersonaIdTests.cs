using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests;

public sealed class PersonaIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void New_WithoutAValue_Throws(string value)
    {
        var create = () => new PersonaId(value);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_IsTheValue()
    {
        new PersonaId("brouwer-dranken").ToString().Should().Be("brouwer-dranken");
    }
}
