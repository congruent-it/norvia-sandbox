using System.Buffers.Binary;
using System.Xml.Linq;

namespace Norvia.Branding.UnitTests;

/// <summary>The icon files are what browsers fetch, so their formats and sizes are checked as shipped.</summary>
public sealed class IconFilesTests
{
    private static string IconPath(string file) => Path.Combine(AppContext.BaseDirectory, "icons", file);

    [Fact]
    public void FaviconSvg_IsWellFormedAndScalesFromItsViewBox()
    {
        var svg = XDocument.Load(IconPath("favicon.svg")).Root!;

        svg.Name.LocalName.Should().Be("svg");
        svg.Attribute("viewBox")!.Value.Should().Be("0 0 64 64");
        svg.Attribute("width").Should().BeNull("a fixed size would stop it scaling to the tab");
    }

    [Fact]
    public void FaviconSvg_HasATransparentBackgroundAndALightAndADarkColourScheme()
    {
        var text = File.ReadAllText(IconPath("favicon.svg"));

        text.Should().NotContain("<rect", "the tab shows its own colour behind the mark");
        text.Should().Contain("@media (prefers-color-scheme: dark)");
    }

    [Fact]
    public void FaviconIco_HoldsThe16_32And48PixelImages()
    {
        var ico = File.ReadAllBytes(IconPath("favicon.ico"));

        BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)).Should().Be(1, "the file is an icon, not a cursor");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        var sizes = Enumerable.Range(0, count).Select(entry => (int)ico[6 + entry * 16]).Order();
        sizes.Should().Equal(16, 32, 48);
    }

    [Fact]
    public void AppleTouchIcon_IsA180PixelPng()
    {
        var png = File.ReadAllBytes(IconPath("apple-touch-icon.png"));

        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)).Should().Be(180);
        BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)).Should().Be(180);
    }
}
