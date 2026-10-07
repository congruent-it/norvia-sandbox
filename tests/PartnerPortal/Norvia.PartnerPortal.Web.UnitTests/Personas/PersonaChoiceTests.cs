using Microsoft.AspNetCore.Http;
using Norvia.PartnerPortal.Web.Personas;

namespace Norvia.PartnerPortal.Web.UnitTests.Personas;

public sealed class PersonaChoiceTests
{
    [Fact]
    public void Current_OnAPersonasPage_IsThatPersona()
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["PersonaId"] = "van-dijk-transport";
        context.Request.Headers.Cookie = $"{PersonaChoice.CookieName}=brouwer-dranken";

        PersonaChoice.Current(context.Request, Samples.Personas).Should().Be(Samples.VanDijkTransport);
    }

    [Fact]
    public void Current_ElsewhereAfterSwitching_IsThePersonaSwitchedTo()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{PersonaChoice.CookieName}=van-dijk-transport";

        PersonaChoice.Current(context.Request, Samples.Personas).Should().Be(Samples.VanDijkTransport);
    }

    [Fact]
    public void Current_BeforeSwitching_IsTheFirstPersona()
    {
        PersonaChoice.Current(new DefaultHttpContext().Request, Samples.Personas).Should().Be(Samples.BrouwerDranken);
    }

    [Fact]
    public void Current_ForAPersonaThereIsNoLongerAny_IsTheFirstPersona()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{PersonaChoice.CookieName}=gone";

        PersonaChoice.Current(context.Request, Samples.Personas).Should().Be(Samples.BrouwerDranken);
    }

    [Fact]
    public void Paths_AreUnderThePersona()
    {
        PersonaChoice.NewBookingPath("brouwer-dranken").Should().Be("/personas/brouwer-dranken/bookings/new");
        PersonaChoice.BookingsPath("brouwer-dranken").Should().Be("/personas/brouwer-dranken/bookings");
        PersonaChoice.NotificationsPath("brouwer-dranken").Should().Be("/personas/brouwer-dranken/notifications");
        PersonaChoice.ShipmentsPath("van-dijk-transport").Should().Be("/personas/van-dijk-transport/shipments");
    }
}
