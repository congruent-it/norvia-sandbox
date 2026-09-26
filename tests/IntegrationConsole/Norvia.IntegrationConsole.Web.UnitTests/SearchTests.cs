using Microsoft.AspNetCore.Authorization;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Trails;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class SearchTests : ConsoleWebContext
{
    [Fact]
    public void Render_ForAnOperator_ShowsTheSearchPage()
    {
        var page = Render<Search>();

        page.Find("h1").TextContent.Should().Be("Search Message trails");
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        // Message trails lead to message bodies, which hold personal data.
        typeof(Search).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }
}
