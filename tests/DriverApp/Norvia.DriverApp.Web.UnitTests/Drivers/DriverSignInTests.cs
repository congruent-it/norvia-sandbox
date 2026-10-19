using Norvia.DriverApp.Web.Drivers;

namespace Norvia.DriverApp.Web.UnitTests.Drivers;

public sealed class DriverSignInTests
{
    [Fact]
    public void IconLinks_LinkTheNorviaIconFromTheSitesRoot()
    {
        DriverSignIn.IconLinks.Should()
            .Contain("href=\"/_content/Norvia.Branding/favicon.svg\"")
            .And.Contain("href=\"/_content/Norvia.Branding/favicon.ico\"")
            .And.Contain("href=\"/_content/Norvia.Branding/apple-touch-icon.png\"");
    }
}
