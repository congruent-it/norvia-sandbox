using Norvia.IntegrationConsole.Web.Trails;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class TrailTextTests
{
    [Theory]
    [InlineData(250, "250 ms")]
    [InlineData(1_500, "1.5 s")]
    [InlineData(42_000, "42 s")]
    [InlineData(125_000, "2 min 5 s")]
    public void Of_ADuration_ReadsAsAnOperatorWouldSayIt(int milliseconds, string text)
    {
        TrailText.Of(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(text);
    }

    [Fact]
    public void Of_NoDuration_IsADash()
    {
        TrailText.Of((TimeSpan?)null).Should().Be("–");
    }
}
