using Norvia.Tms.Web.Components.Pages;

namespace Norvia.Tms.Web.UnitTests;

public sealed class ErrorPageTests : BunitContext
{
    [Fact]
    public void Render_SaysSomethingWentWrongWithoutDetailsOfTheFailure()
    {
        var page = Render<Error>();

        page.Find("h1").TextContent.Should().Be("Something went wrong");
        page.Markup.Should().NotContain("Exception");
    }
}
