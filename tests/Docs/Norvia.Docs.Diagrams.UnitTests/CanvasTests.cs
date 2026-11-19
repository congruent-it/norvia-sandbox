namespace Norvia.Docs.Diagrams.UnitTests;

public sealed class CanvasTests
{
    [Theory]
    [InlineData("a/very/long/path/segment/another/segment/and/more", 15, 120, "a/very/long|/path/segment|/another|/segment/and|/more")]
    [InlineData("a-very-long-identifier-with-hyphens-and-words-in-it-all-over", 12, 100, "a-very-long|-identifier|-with-hyphens|-and-words-in|-it-all-over")]
    [InlineData("plain words that wrap at a width", 15, 90, "plain|words that|wrap at a|width")]
    [InlineData("", 12, 50, "")]
    [InlineData("one\ntwo three four", 12, 40, "one|two|three|four")]
    [InlineData("/leading/slash/path/that/is/long/enough", 12, 80, "/leading|/slash/path|/that/is|/long|/enough")]
    public void Wrap_AtAConservativeWidth_BreaksWhereTheFormerGeneratorDid(string text, int fontSize, int width, string expected)
    {
        Canvas.Wrap(text, fontSize, width).Should().Equal(expected.Split('|'));
    }

    [Fact]
    public void TextHeight_CountsTheWrappedLines()
    {
        Canvas.TextHeight("plain words that wrap at a width", 15, 90).Should().Be((Num)(4 * 15 * 1.25));
        Canvas.TextHeight("one line", 12).ToString().Should().Be("15.0");
    }

    [Fact]
    public void Save_EveryPrimitive_WritesWhatTheFormerGeneratorWrote()
    {
        var cv = new Canvas();
        cv.Rect(10, 20, 30, 40, "#ffffff", "#000000");
        cv.Ellipse(5, 5, 20, 20, "#fff", "#000", 2, dashed: true);
        cv.Diamond(1, 2, 3, 4, "#fff", "#000");
        cv.VerticalText(100, 100, 80, "A lane's label & more <here>", 12, "#ffffff", bold: true);
        cv.DrawArrow([new Pt(0, 0), new Pt(10, 0), new Pt(10, 10)], "#495057", false, true, "a", "b");
        cv.DrawArrow([new Pt(0, 0), new Pt(5.5, 5.5)], "#495057", true, false, null, null, plain: true);
        cv.Text(1, 2, 100, "left");
        cv.Text(1, 2, 100, "right", 12, "#000", "right");

        using var folder = new TempFolder();
        cv.Save(200, 300, folder.Path("canvas.excalidraw"), folder.Path("canvas.svg"));

        File.ReadAllText(folder.Path("canvas.excalidraw")).Should().Be(Golden.Read("canvas.excalidraw"));
        File.ReadAllText(folder.Path("canvas.svg")).Should().Be(Golden.Read("canvas.svg"));
    }

    [Fact]
    public void Save_WithoutAPreviewPath_WritesTheExcalidrawOnly()
    {
        var cv = new Canvas();
        cv.Rect(0, 0, 1, 1, "#fff", "#000", radius: 0);

        using var folder = new TempFolder();
        cv.Save(10, 10, folder.Path("one.excalidraw"));

        Directory.GetFiles(folder.Directory).Select(Path.GetFileName).Should().Equal("one.excalidraw");
        cv.Elements.Should().ContainSingle().Which["roundness"].Should().BeNull();
    }

    [Fact]
    public void Rect_Id_IsGivenOrNumberedByKind()
    {
        var cv = new Canvas();
        var named = cv.Rect(0, 0, 1, 1, "#fff", "#000", id: "box-a");
        var numbered = cv.Rect(0, 0, 1, 1, "#fff", "#000");

        named.Id.Should().Be("box-a");
        numbered.Id.Should().Be("rec0002");
        named.BoundElements.Should().BeEmpty();
    }
}
