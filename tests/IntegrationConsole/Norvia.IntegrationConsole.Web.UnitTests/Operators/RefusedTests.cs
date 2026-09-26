using Microsoft.AspNetCore.Http;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests.Operators;

public sealed class RefusedTests : ConsoleWebContext
{
    [Fact]
    public void Refused_TellsTheOperatorWhichRoleTheyLack_AndAnswersForbidden()
    {
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Resubmit);
        var request = new DefaultHttpContext();

        var page = Render<Refused>(parameters => parameters.AddCascadingValue(request));

        page.Find("h1").TextContent.Should().Be("You can't use the Integration console");
        page.Markup.Should().Contain("Iris Operator").And.Contain(OperatorRoles.Read);
        request.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
