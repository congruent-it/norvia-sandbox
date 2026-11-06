using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Domain.UnitTests.Personas;

public sealed class TextPoolTests
{
    private static readonly TextPool Pool = new("terse-dutch", "nl", TextCategory.DeclineReason, TextPool.BuiltInSource, ["a", "b", "c", "d", "e", "f", "g", "h"]);

    [Fact]
    public void Draw_TheSameSeedAndStep_DrawsTheSameTextEveryTime()
    {
        Pool.Draw(42, 5).Should().BeEquivalentTo(Pool.Draw(42, 5));
        Enumerable.Range(1, 20).Select(step => Pool.Draw(7, step).Index).Should().Equal(Enumerable.Range(1, 20).Select(step => Pool.Draw(7, step).Index));
    }

    [Fact]
    public void Draw_AcrossStepsAndSeeds_UsesMoreThanOneText()
    {
        Enumerable.Range(1, 40).Select(step => Pool.Draw(7, step).Index).Distinct().Count().Should().BeGreaterThan(3);
        Enumerable.Range(1, 40).Select(seed => Pool.Draw(seed, 1).Index).Distinct().Count().Should().BeGreaterThan(3);
    }

    [Fact]
    public void Draw_AnIndexAndATextFromThePool()
    {
        var draw = Pool.Draw(3, 9);

        draw.Text.Should().Be(Pool.Texts[draw.Index]);
        draw.Pool.Should().BeSameAs(Pool);
    }

    [Fact]
    public void Draw_TheReference_NamesThePoolAndTheIndexButNeverTheText()
    {
        var draw = Pool.Draw(3, 9);

        draw.Reference.Should().Be($"pool terse-dutch/DeclineReason/nl, index {draw.Index}");
        draw.Reference.Should().NotContain(draw.Text + "x");
    }

    [Fact]
    public void Draw_TheTwoKindsOfText_DoNotMoveInStep()
    {
        var remarks = Pool with { Category = TextCategory.DeliveryFailedRemark };

        Enumerable.Range(1, 30).Select(step => Pool.Draw(5, step).Index).Should().NotEqual(Enumerable.Range(1, 30).Select(step => remarks.Draw(5, step).Index));
    }

    [Fact]
    public void Draw_AnEmptyPool_Throws()
    {
        var act = () => (Pool with { Texts = [] }).Draw(1, 1);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AgentSource_NamesTheRun()
    {
        TextPool.AgentSource(12).Should().Be("agent, run 12");
    }
}
