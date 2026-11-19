using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class ConsigneeEmailTests
{
    [Theory]
    [InlineData("name@example.com", true)]
    [InlineData("  name@example.com  ", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("name@example", false)]
    [InlineData("name@.example.com", false)]
    [InlineData("name@example.com.", false)]
    [InlineData("Name <name@example.com>", false)]
    [InlineData("name@example.com;other@example.com", false)]
    public void IsValid_OnePlainAddress_IsTheOnlyThingThatPasses(string? text, bool expected) =>
        ConsigneeEmail.IsValid(text).Should().Be(expected);

    [Fact]
    public void Consignee_NeverPrintsItsNamePhoneOrEmail_SoNothingThatLogsItLeaksThem()
    {
        var consignee = new Consignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142", "inkoop@dezwaan.example");

        var text = consignee.ToString();

        text.Should().NotContain("Zwaan").And.NotContain("Sanne").And.NotContain("555").And.NotContain("inkoop");
        new DeliveryDetails(null, consignee, null).ToString().Should().NotContain("inkoop");
    }
}
