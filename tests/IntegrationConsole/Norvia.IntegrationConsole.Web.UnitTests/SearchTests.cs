using Microsoft.AspNetCore.Authorization;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Trails;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class SearchTests : ConsoleWebContext
{
    [Fact]
    public void Search_IsWhereAnOperatorLands()
    {
        var page = Render<Search>();

        page.Find("h1").TextContent.Should().Be("Search Message trails");
    }

    [Fact]
    public void Search_NeedsTheReadRole()
    {
        // Message trails lead to message bodies, which hold personal data.
        typeof(Search).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }
}
