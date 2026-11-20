namespace Norvia.Docs.LinkCheck.UnitTests;

public sealed class DeadLinkFinderTests
{
    private static Func<string, bool> Existing(params string[] paths) => paths.ToHashSet(StringComparer.Ordinal).Contains;

    private static string[] Find(string file, string markdown, params string[] existing) =>
        [.. DeadLinkFinder.Find(file, markdown, Existing(existing)).Select(d => d.ToString())];

    [Fact]
    public void Find_MissingTarget_IsReportedAsFileLineArrowTarget()
    {
        Find("docs/a.md", "x\n[gone](gone.md)\n").Should().Equal("docs/a.md:2 -> gone.md");
    }

    [Fact]
    public void Find_TargetResolvesAgainstTheFilesDirectory()
    {
        Find("docs/a.md", "[b](b.md)", "docs/b.md").Should().BeEmpty();
        Find("docs/a.md", "[b](b.md)", "b.md").Should().Equal("docs/a.md:1 -> b.md");
    }

    [Fact]
    public void Find_FileInTheRoot_ResolvesAgainstTheRoot()
    {
        Find("README.md", "[b](docs/b.md)", "docs/b.md").Should().BeEmpty();
    }

    [Fact]
    public void Find_ParentSegments_AreResolved()
    {
        Find("docs/adr/a.md", "[r](../../README.md) [s](./sibling.md)", "README.md", "docs/adr/sibling.md").Should().BeEmpty();
    }

    [Fact]
    public void Find_ParentSegmentsAboveTheRoot_AreDead()
    {
        Find("a.md", "[r](../outside.md)", "outside.md").Should().Equal("a.md:1 -> ../outside.md");
    }

    [Fact]
    public void Find_FragmentAndQuery_AreStrippedBeforeResolving()
    {
        Find("a.md", "[x](b.md#part) [y](b.md?v=1) [z](b.md?v=1#part)", "b.md").Should().BeEmpty();
    }

    [Fact]
    public void Find_FragmentOnAMissingFile_IsDeadWithTheTargetAsWritten()
    {
        Find("a.md", "[x](gone.md#part)").Should().Equal("a.md:1 -> gone.md#part");
    }

    [Fact]
    public void Find_QueryOnly_PointsAtTheFileItself()
    {
        Find("a.md", "[x](?plain=1)").Should().BeEmpty();
    }

    [Fact]
    public void Find_PercentEncodedSpace_IsDecoded()
    {
        Find("a.md", "[x](my%20file.md)", "my file.md").Should().BeEmpty();
    }

    [Fact]
    public void Find_BadPercentEncoding_IsTakenLiterally()
    {
        Find("a.md", "[x](100%.md)", "100%.md").Should().BeEmpty();
    }

    [Fact]
    public void Find_DirectoryTarget_PassesWithOrWithoutTrailingSlash()
    {
        Find("docs/a.md", "[x](operations/) [y](operations)", "docs/operations").Should().BeEmpty();
    }

    [Fact]
    public void Find_RootRelativeTarget_ResolvesAgainstTheRoot()
    {
        Find("docs/a.md", "[x](/README.md)", "README.md").Should().BeEmpty();
    }

    [Fact]
    public void Find_WrongCaseName_IsDeadWhenTheFileSystemSaysSo()
    {
        Find("a.md", "[x](Readme.md)", "README.md").Should().Equal("a.md:1 -> Readme.md");
    }

    [Fact]
    public void Find_SeveralDeadLinks_AreReportedInOrder()
    {
        Find("a.md", "[1](x.md)\n[2](y.md)\n[3](b.md)", "b.md").Should().Equal("a.md:1 -> x.md", "a.md:2 -> y.md");
    }
}
