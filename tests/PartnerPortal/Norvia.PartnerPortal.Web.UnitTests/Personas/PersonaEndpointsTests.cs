using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.Personas;

namespace Norvia.PartnerPortal.Web.UnitTests.Personas;

public sealed class PersonaEndpointsTests
{
    private readonly IPartnerPortal _portal = Substitute.For<IPartnerPortal>();

    public PersonaEndpointsTests() => _portal.ListPersonasAsync(Arg.Any<CancellationToken>()).Returns(Samples.Personas);

    [Fact]
    public async Task OpenHome_BeforeSwitching_OpensTheFirstPersonasBookingForm()
    {
        var result = await PersonaEndpoints.OpenHomeAsync(new DefaultHttpContext().Request, _portal);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be("/personas/brouwer-dranken/bookings/new");
    }

    [Fact]
    public async Task OpenHome_AfterSwitching_OpensThatPersonasBookingForm()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{PersonaChoice.CookieName}=van-dijk-transport";

        var result = await PersonaEndpoints.OpenHomeAsync(context.Request, _portal);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be("/personas/van-dijk-transport/bookings/new");
    }

    [Fact]
    public async Task Switch_ToAPersona_RemembersItAndOpensItsBookingForm()
    {
        var context = new DefaultHttpContext();

        var result = await PersonaEndpoints.SwitchAsync("van-dijk-transport", context.Response, _portal);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be("/personas/van-dijk-transport/bookings/new");
        context.Response.Headers.SetCookie.ToString().Should().StartWith($"{PersonaChoice.CookieName}=van-dijk-transport").And.Contain("httponly");
    }

    [Fact]
    public async Task Switch_ToAPersonaThereIsNone_IsABadRequest_AndRemembersNothing()
    {
        var context = new DefaultHttpContext();

        var result = await PersonaEndpoints.SwitchAsync("nobody", context.Response, _portal);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }
}
