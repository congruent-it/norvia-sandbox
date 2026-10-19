using Norvia.Branding;

namespace Norvia.Branding.UnitTests;

public sealed class NorviaFaviconTests : BunitContext
{
    [Fact]
    public void Render_LinksTheSvgIconFirst()
    {
        var cut = Render<NorviaFavicon>();

        var icon = cut.Find("link[rel=icon][type='image/svg+xml']");
        icon.GetAttribute("href").Should().Be("_content/Norvia.Branding/favicon.svg");
        icon.GetAttribute("sizes").Should().Be("any");
    }

    [Fact]
    public void Render_LinksTheIcoAsFallbackForThreeSizes()
    {
        var cut = Render<NorviaFavicon>();

        var icon = cut.Find("link[rel=icon][href$='favicon.ico']");
        icon.GetAttribute("href").Should().Be("_content/Norvia.Branding/favicon.ico");
        icon.GetAttribute("sizes").Should().Be("16x16 32x32 48x48");
    }

    [Fact]
    public void Render_LinksTheAppleTouchIcon()
    {
        var cut = Render<NorviaFavicon>();

        cut.Find("link[rel=apple-touch-icon]").GetAttribute("href").Should().Be("_content/Norvia.Branding/apple-touch-icon.png");
    }
}
