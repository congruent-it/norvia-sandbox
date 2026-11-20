namespace Norvia.Docs.LinkCheck.UnitTests;

public sealed class MarkdownLinksTests
{
    [Fact]
    public void Extract_InlineLink_ReturnsTargetAndLine()
    {
        var links = MarkdownLinks.Extract("# Title\n\nSee [the guide](docs/guide.md) now.\n");

        links.Should().Equal(new MarkdownLink(3, "docs/guide.md"));
    }

    [Theory]
    [InlineData("[a](http://example.com/x.md)")]
    [InlineData("[a](https://example.com/x.md)")]
    [InlineData("[a](HTTPS://example.com/x.md)")]
    [InlineData("[a](mailto:someone@example.com)")]
    [InlineData("<https://example.com/x.md>")]
    [InlineData("https://example.com/x.md")]
    [InlineData("[a](//example.com/x.md)")]
    public void Extract_UrlLink_IsSkipped(string markdown)
    {
        MarkdownLinks.Extract(markdown).Should().BeEmpty();
    }

    [Fact]
    public void Extract_PureAnchor_IsSkipped()
    {
        MarkdownLinks.Extract("[top](#title)").Should().BeEmpty();
    }

    [Fact]
    public void Extract_FencedCodeBlock_IsSkipped()
    {
        var links = MarkdownLinks.Extract("```md\n[a](missing.md)\n```\n\n~~~\n[b](missing.md)\n~~~\n");

        links.Should().BeEmpty();
    }

    [Fact]
    public void Extract_IndentedCodeBlock_IsSkipped()
    {
        MarkdownLinks.Extract("Text\n\n    [a](missing.md)\n").Should().BeEmpty();
    }

    [Fact]
    public void Extract_InlineCode_IsSkipped()
    {
        MarkdownLinks.Extract("Write `[a](missing.md)` like this.").Should().BeEmpty();
    }

    [Fact]
    public void Extract_ImageAndReferenceLink_AreFound()
    {
        var links = MarkdownLinks.Extract("![alt](pics/a.png)\n\n[ref][r]\n\n[r]: other/b.md\n");

        links.Should().Equal(new MarkdownLink(1, "pics/a.png"), new MarkdownLink(3, "other/b.md"));
    }

    [Fact]
    public void Extract_LinkInTableCell_IsFound()
    {
        var links = MarkdownLinks.Extract("| A |\n|---|\n| [x](x.md) |\n");

        links.Should().Equal(new MarkdownLink(3, "x.md"));
    }

    [Fact]
    public void Extract_FragmentAndQuery_AreKeptInTheTarget()
    {
        var links = MarkdownLinks.Extract("[a](x.md#part) [b](y.md?v=1)");

        links.Select(l => l.Target).Should().Equal("x.md#part", "y.md?v=1");
    }

    [Fact]
    public void Extract_TargetInAngleBrackets_IsUnwrapped()
    {
        var links = MarkdownLinks.Extract("[a](<my file.md>)");

        links.Should().Equal(new MarkdownLink(1, "my file.md"));
    }
}
