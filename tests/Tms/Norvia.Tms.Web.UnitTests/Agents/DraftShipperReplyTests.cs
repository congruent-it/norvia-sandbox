using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents.Reply;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class DraftShipperReplyTests : TmsWebContext
{
    private const string Number = "TO-0000042";

    private static Suggestion Answer(string draft = "Hello,\n\nIt is in transit.\n\nKind regards,\nNorvia Customer service") => new(
        draft,
        [new Evidence("The Tracking status is InTransit.", "r1", "get_shipper_view")],
        "Edit the draft, then send it to the Shipper yourself: the TMS sends nothing.",
        Confidence.High);

    private IRenderedComponent<DraftShipperReply> Render(bool customerService = true) =>
        Render<DraftShipperReply>(parameters => parameters.Add(page => page.TransportOrderNumber, Number).Add(page => page.ForCustomerService, customerService));

    [Fact]
    public void Render_AgentsOffOrAnotherRole_ShowsNothing()
    {
        AgentSettings.Enabled = false;
        Render().FindAll("fluent-button").Should().BeEmpty();

        AgentSettings.Enabled = true;
        Render(customerService: false).FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_OffersTheThreeQuestions()
    {
        AgentSettings.Enabled = true;

        var page = Render();

        page.FindAll("fluent-option").Select(option => option.TextContent.Trim())
            .Should().Equal("Where is my order?", "Has it been delivered?", "Is there a Proof of delivery?");
    }

    [Fact]
    public void Draft_ShowsAnEditableDraft_AndSendsNothing()
    {
        AgentSettings.Enabled = true;
        Explain.DraftAsync(new DraftShipperReplyQuery(Number, ShipperQuestion.WhereIsMyOrder), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=reply-draft] fluent-text-area"));
        page.Find("[data-testid=reply-draft]").TextContent.Should().Contain("the TMS sends nothing and keeps nothing");
        Commands.ReceivedCalls().Should().BeEmpty("the TMS sends nothing");
    }

    [Fact]
    public void Draft_TheDraftIsAnEditableTextArea_WithTheAssistantsTextEncoded()
    {
        AgentSettings.Enabled = true;
        Explain.DraftAsync(Arg.Any<DraftShipperReplyQuery>(), Arg.Any<CancellationToken>()).Returns(Answer("<script>alert(1)</script>"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("fluent-text-area"));
        page.FindAll("script").Should().BeEmpty();
        page.Find("fluent-text-area").GetAttribute("value").Should().Be("<script>alert(1)</script>");
    }

    [Fact]
    public void Draft_WhenAnotherQuestionIsChosen_AsksThatQuestion()
    {
        AgentSettings.Enabled = true;
        Explain.DraftAsync(Arg.Any<DraftShipperReplyQuery>(), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-select").Change(nameof(ShipperQuestion.IsThereProofOfDelivery));
        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => Explain.Received(1).DraftAsync(new DraftShipperReplyQuery(Number, ShipperQuestion.IsThereProofOfDelivery), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Draft_WhenTheAgentIsUnavailableOrThrows_ShowsAssistantUnavailableAndNoDraft()
    {
        AgentSettings.Enabled = true;
        Explain.DraftAsync(Arg.Any<DraftShipperReplyQuery>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(Result<Suggestion>.Unavailable("AgentUnavailable")),
            Task.FromException<Result<Suggestion>>(new InvalidOperationException("boom")));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var page = Render();
            page.Find("fluent-button").Click();
            page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
            page.FindAll("[data-testid=reply-draft]").Should().BeEmpty();
        }
    }
}
