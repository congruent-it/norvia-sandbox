using Norvia.IntegrationConsole.Application.Trails.Open;
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

    [Fact]
    public void Of_ForAHandlingWithoutATime_SaysItIsUnknown()
    {
        TrailText.Of(new DeadLetterHandling(DeadLetterStatus.Resubmitted, "q", null, "ana", null, null)).Should().Be("Resubmitted by ana on an unknown time");
    }

    [Fact]
    public void Of_ForAMessageOnTheQueue_SaysSo()
    {
        TrailText.Of(new DeadLetterHandling(DeadLetterStatus.OnQueue, "q", 1, null, null, null)).Should().Be("On the dead-letter queue");
    }
}
