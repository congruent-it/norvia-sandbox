using Norvia.PartnerPortal.Application.TrackingAssistant;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.ShipperTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

public sealed class TrackingAssistantPanelTests : PortalWebContext
{
    private static readonly PersonaId Brouwer = new("brouwer-dranken");

    private IRenderedComponent<TrackingAssistantPanel> RenderPanel(string? open = "TO-0000042") =>
        Render<TrackingAssistantPanel>(parameters => parameters.Add(panel => panel.PersonaId, Brouwer).Add(panel => panel.OpenOrderNumber, open));

    private void Answer(AssistantAnswer answer) =>
        Portal.AskTrackingAssistantAsync(Brouwer, Arg.Any<string>(), Arg.Any<IReadOnlyList<AssistantTurn>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(answer);

    private static void Ask(IRenderedComponent<TrackingAssistantPanel> panel, string question)
    {
        panel.Find("#assistant-question").Input(question);
        panel.Find("form").Submit();
    }

    [Fact]
    public void Panel_WhileTheAssistantIsOff_ShowsNothing()
    {
        Portal.TrackingAssistantEnabled.Returns(false);

        RenderPanel().Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Panel_ShowsTheAnswerBesideTheQuestion_AndAsksAboutTheOpenOrder()
    {
        Portal.TrackingAssistantEnabled.Returns(true);
        Answer(new AssistantAnswer(AssistantOutcome.Answered, "TO-0000042 is In transit.", null, "run-1"));
        var panel = RenderPanel();

        Ask(panel, "Where is my order?");

        panel.Find("[data-testid=assistant-answer]").TextContent.Should().Be("TO-0000042 is In transit.");
        panel.Find("ul[aria-label=Conversation]").TextContent.Should().Contain("Where is my order?");
        Portal.Received(1).AskTrackingAssistantAsync(Brouwer, "Where is my order?", Arg.Is<IReadOnlyList<AssistantTurn>>(h => h.Count == 0), "TO-0000042", Arg.Any<CancellationToken>());
        panel.Find("#assistant-question").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void Panel_ShowsAnAnswerAsPlainText_NeverAsMarkup()
    {
        Portal.TrackingAssistantEnabled.Returns(true);
        Answer(new AssistantAnswer(AssistantOutcome.Answered, "<script>alert(1)</script> <b>bold</b>", null, "run-1"));
        var panel = RenderPanel();

        Ask(panel, "hi");

        panel.FindAll("[data-testid=assistant-answer] script, [data-testid=assistant-answer] b").Should().BeEmpty();
    }

    [Theory]
    [InlineData("You have asked a lot of questions. Try again in a few minutes.", "AgentRateLimited")]
    [InlineData("Assistant unavailable", "AgentUnavailable")]
    [InlineData("I can't reach tracking right now", "TmsUnavailable")]
    [InlineData("I have no information about that.", "AgentAnswerRejected")]
    public void Panel_ShowsTheFixedTextOfAFailureInAStatusMessage(string text, string failureCode)
    {
        Portal.TrackingAssistantEnabled.Returns(true);
        Answer(new AssistantAnswer(AssistantOutcome.Failed, text, failureCode, "run-1"));
        var panel = RenderPanel();

        Ask(panel, "Where is my order?");

        panel.Find("[data-testid=assistant-notice]").TextContent.Should().Contain(text);
        panel.FindAll("[data-testid=assistant-answer]").Should().BeEmpty();
    }

    [Fact]
    public void Panel_WhenTheAssistantThrows_SaysItIsUnavailable_InTheQuestionsLanguage()
    {
        Portal.TrackingAssistantEnabled.Returns(true);
        Portal.AskTrackingAssistantAsync(Brouwer, Arg.Any<string>(), Arg.Any<IReadOnlyList<AssistantTurn>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<AssistantAnswer>(_ => throw new InvalidOperationException("boom"));
        var panel = RenderPanel();

        Ask(panel, "Waar is mijn bestelling?");

        panel.Find("[data-testid=assistant-notice]").TextContent.Should().Contain("Assistent niet beschikbaar");
    }

    [Fact]
    public void Panel_SendsTheEarlierAnsweredTurnsWithTheNextQuestion_ButNotAFailedOne()
    {
        Portal.TrackingAssistantEnabled.Returns(true);
        var panel = RenderPanel(open: null);
        Answer(new AssistantAnswer(AssistantOutcome.Answered, "first answer", null, "run-1"));
        Ask(panel, "first");
        Answer(new AssistantAnswer(AssistantOutcome.Failed, "Assistant unavailable", "AgentUnavailable", "run-2"));
        Ask(panel, "second");

        Ask(panel, "third");

        Portal.Received(1).AskTrackingAssistantAsync(
            Brouwer, "third", Arg.Is<IReadOnlyList<AssistantTurn>>(history => history.Count == 1 && history[0].Question == "first" && history[0].Answer == "first answer"), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Conversation_KeepsTheLastFourAnsweredTurns()
    {
        var conversation = new AssistantConversation();
        for (var i = 1; i <= 6; i++)
        {
            conversation.Add($"q{i}", new AssistantAnswer(AssistantOutcome.Answered, $"a{i}", null, null));
        }

        conversation.History.Select(turn => turn.Question).Should().Equal("q3", "q4", "q5", "q6");
    }
}
