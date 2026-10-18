using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Web.Roles;

namespace Norvia.Tms.Web.UnitTests.Roles;

public sealed class RoleEndpointsTests
{
    [Fact]
    public void OpenHome_WithNoRoleChosen_OpensThePlannersHomePage()
    {
        var context = new DefaultHttpContext();

        var result = RoleEndpoints.OpenHome(context.Request);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be("/home");
    }

    [Fact]
    public void OpenHome_AsCustomerService_OpensTheRejectedBookings()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{NorviaRole.CookieName}=customer-service";

        var result = RoleEndpoints.OpenHome(context.Request);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be("/rejected-bookings");
    }

    [Fact]
    public void OpenHome_WithARoleThatDoesntExist_OpensThePlannersHomePage()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{NorviaRole.CookieName}=administrator";

        var result = RoleEndpoints.OpenHome(context.Request);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be("/home");
    }

    [Theory]
    [InlineData("customer-service", "/rejected-bookings")]
    [InlineData("planner", "/home")]
    public void Switch_ToARole_RemembersItAndOpensItsHome(string role, string view)
    {
        var context = new DefaultHttpContext();

        var result = RoleEndpoints.Switch(role, context.Response);

        result.Should().BeOfType<RedirectHttpResult>().Which.Url.Should().Be(view);
        context.Response.Headers.SetCookie.ToString().Should().StartWith($"{NorviaRole.CookieName}={role};");
    }

    [Fact]
    public void Switch_ToARoleThatDoesntExist_IsABadRequestAndChangesNothing()
    {
        var context = new DefaultHttpContext();

        var result = RoleEndpoints.Switch("administrator", context.Response);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }
}
