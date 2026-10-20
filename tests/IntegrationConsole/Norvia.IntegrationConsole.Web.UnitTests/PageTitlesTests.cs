namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class PageTitlesTests
{
    [Theory]
    [InlineData("5284193b-55df-4538-9820-cc2c40f4bd2a", "5284193b")]
    [InlineData("short", "short")]
    [InlineData("12345678", "12345678")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void ShortId_KeepsTheStartOfAnId(string? id, string expected) => PageTitles.ShortId(id).Should().Be(expected);
}
