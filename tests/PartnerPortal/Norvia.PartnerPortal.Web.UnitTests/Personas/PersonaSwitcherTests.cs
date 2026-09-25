using Norvia.PartnerPortal.Web.Personas;

namespace Norvia.PartnerPortal.Web.UnitTests.Personas;

public sealed class PersonaSwitcherTests : PortalWebContext
{
    [Fact]
    public void Render_OffersEveryPersona_WithTheCurrentOnePressed()
    {
        var switcher = Render<PersonaSwitcher>(parameters => parameters
            .Add(component => component.Personas, Samples.Personas)
            .Add(component => component.Current, Samples.VanDijkTransport));

        switcher.FindAll("button").Select(button => (button.TextContent.Trim(), button.GetAttribute("value"), button.GetAttribute("aria-pressed")))
            .Should().Equal(("Brouwer Dranken", "brouwer-dranken", "false"), ("Van Dijk Transport", "van-dijk-transport", "true"));
        switcher.Find("form").GetAttribute("action").Should().Be("/persona");
    }
}
