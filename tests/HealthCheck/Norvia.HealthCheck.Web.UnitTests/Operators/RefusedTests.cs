using Microsoft.AspNetCore.Http;
using Norvia.HealthCheck.Application.Operators;
using Norvia.HealthCheck.Web.Operators;

namespace Norvia.HealthCheck.Web.UnitTests.Operators;

public sealed class RefusedTests : HealthCheckWebContext
{
    [Fact]
    public void Render_ForAnOperatorWithoutTheRunRole_TellsThemWhichRoleTheyLack_AndAnswersForbidden()
    {
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read);
        var request = new DefaultHttpContext();

        var page = Render<Refused>(parameters => parameters.AddCascadingValue(request));

        page.Find("h1").TextContent.Should().Be("You can't run an Estate health check");
        page.Markup.Should().Contain("Iris Operator").And.Contain(OperatorRoles.Run);
        request.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
