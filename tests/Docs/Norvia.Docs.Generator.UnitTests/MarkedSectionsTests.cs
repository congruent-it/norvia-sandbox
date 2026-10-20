namespace Norvia.Docs.Generator.UnitTests;

public sealed class MarkedSectionsTests
{
    private const string Page = "# Title\n\nBefore.\n\n<!-- generated:database -->\nold\n<!-- /generated:database -->\n\nAfter.\n";

    [Fact]
    public void Replace_TheMarkedSection_SwapsItsContentAndKeepsTheRest()
    {
        var page = MarkedSections.Replace(Page, "database", "new text\n");

        page.Should().Be("# Title\n\nBefore.\n\n<!-- generated:database -->\n\nnew text\n\n<!-- /generated:database -->\n\nAfter.\n");
    }

    [Fact]
    public void Replace_WithTheSameContentTwice_ChangesNothingTheSecondTime()
    {
        var once = MarkedSections.Replace(Page, "database", "new text");

        MarkedSections.Replace(once, "database", "new text").Should().Be(once);
    }

    [Fact]
    public void Replace_APageWithoutTheStartMarker_Throws()
    {
        var replace = () => MarkedSections.Replace("# Title\n", "database", "x");

        replace.Should().Throw<InvalidOperationException>().WithMessage("*<!-- generated:database -->*");
    }

    [Fact]
    public void Replace_APageWithoutTheEndMarker_Throws()
    {
        var replace = () => MarkedSections.Replace("<!-- generated:database -->\nold\n", "database", "x");

        replace.Should().Throw<InvalidOperationException>().WithMessage("*<!-- /generated:database -->*");
    }
}
