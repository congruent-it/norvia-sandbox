using Ardalis.Result;
using Microsoft.AspNetCore.Components;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.TransportOrders;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class RecommendReleaseTests : TmsWebContext
{
    private const string Number = "TO-0000042";
    private static readonly AgentRun Run = new("customer-service", "run-7");

    private static Suggestion Answer(SuggestedActionKind kind, AgentRun? run = null) => new(
        "No Carrier has sent a Proof of delivery in time.",
        [new Evidence("The TMS says the order may be released.", "r1", "get_release_eligibility")],
        "Release it once you have what you rely on instead.",
        Confidence.Medium,
        kind,
        run);

    private IRenderedComponent<RecommendRelease> Render(Action<AgentRun>? onReview = null, bool customerService = true) =>
        Render<RecommendRelease>(parameters => parameters
            .Add(page => page.TransportOrderNumber, Number)
            .Add(page => page.ForCustomerService, customerService)
            .Add(page => page.OnReview, EventCallback.Factory.Create<AgentRun>(this, run => onReview?.Invoke(run))));

    [Fact]
    public void Render_AgentsOffOrAnotherRole_ShowsNoButton()
    {
        AgentSettings.Enabled = false;
        Render().FindAll("fluent-button").Should().BeEmpty();

        AgentSettings.Enabled = true;
        Render(customerService: false).FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Recommend_AReleaseSuggestion_ShowsItWithEvidence_AndOffersToReviewTheRelease_WithoutReleasing()
    {
        AgentSettings.Enabled = true;
        Explain.RecommendAsync(new RecommendReleaseQuery(Number), Arg.Any<CancellationToken>()).Returns(Answer(SuggestedActionKind.Release, Run));
        AgentRun? reviewed = null;
        var page = Render(run => reviewed = run);

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=action-kind]").TextContent.Should().Be("Release"));
        page.Find("[data-testid=recommendation]").TextContent.Should().Contain("[r1, get_release_eligibility]");
        page.Find("fluent-button[aria-label='Review release of TO-0000042']").Click();
        reviewed.Should().Be(Run);
        Commands.ReceivedCalls().Should().BeEmpty("a Release suggestion only pre-selects the order");
    }

    [Theory]
    [InlineData(SuggestedActionKind.Wait, "Wait")]
    [InlineData(SuggestedActionKind.NeedsAPerson, "Needs a person")]
    public void Recommend_AnythingButRelease_OffersNoReview(SuggestedActionKind kind, string text)
    {
        AgentSettings.Enabled = true;
        Explain.RecommendAsync(Arg.Any<RecommendReleaseQuery>(), Arg.Any<CancellationToken>()).Returns(Answer(kind, Run));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=action-kind]").TextContent.Should().Be(text));
        page.FindAll("fluent-button[aria-label^='Review release']").Should().BeEmpty();
    }

    [Fact]
    public void Recommend_AReleaseWithoutARun_OffersNoReview_SoNothingCouldBeAttributed()
    {
        AgentSettings.Enabled = true;
        Explain.RecommendAsync(Arg.Any<RecommendReleaseQuery>(), Arg.Any<CancellationToken>()).Returns(Answer(SuggestedActionKind.Release));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=recommendation]"));
        page.FindAll("fluent-button[aria-label^='Review release']").Should().BeEmpty();
    }

    [Fact]
    public void Recommend_WhatTheAssistantWrote_IsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.RecommendAsync(Arg.Any<RecommendReleaseQuery>(), Arg.Any<CancellationToken>()).Returns(Answer(SuggestedActionKind.Wait) with { Cause = "<script>alert(1)</script>" });
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=recommendation]").TextContent.Should().Contain("<script>alert(1)</script>"));
        page.FindAll("script").Should().BeEmpty();
    }

    [Fact]
    public void Recommend_WhenTheAgentIsUnavailableOrThrows_ShowsAssistantUnavailableAndNothingElse()
    {
        AgentSettings.Enabled = true;
        Explain.RecommendAsync(Arg.Any<RecommendReleaseQuery>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(Result<Suggestion>.Unavailable("AgentUnavailable")),
            Task.FromException<Result<Suggestion>>(new InvalidOperationException("boom")));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var page = Render();
            page.Find("fluent-button").Click();
            page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
            page.FindAll("[data-testid=recommendation]").Should().BeEmpty();
        }
    }

    [Fact]
    public void ReleasePanel_ActingOnASuggestion_SendsTheRunWithTheStaffsRemark()
    {
        Commands.ReleaseWithoutProofOfDeliveryAsync(Number, "CMR by post", Run, Arg.Any<CancellationToken>())
            .Returns(new ReleaseWithoutProofOfDeliveryAnswer.Released(BillingState.Billable));
        var page = Render<ReleaseWithoutProofOfDeliveryPanel>(parameters => parameters
            .Add(panel => panel.TransportOrderNumber, Number).Add(panel => panel.ActingOn, Run)
            .Add(panel => panel.OnClosed, EventCallback.Empty).Add(panel => panel.OnDone, EventCallback.Factory.Create<string>(this, _ => { })));

        page.Find("fluent-text-area").Input("CMR by post");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Commands.Received(1).ReleaseWithoutProofOfDeliveryAsync(Number, "CMR by post", Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ReleasePanel_OnTheStaffsOwn_SendsNoRun()
    {
        Commands.ReleaseWithoutProofOfDeliveryAsync(Number, "CMR by post", null, Arg.Any<CancellationToken>())
            .Returns(new ReleaseWithoutProofOfDeliveryAnswer.Released(BillingState.Billable));
        var page = Render<ReleaseWithoutProofOfDeliveryPanel>(parameters => parameters
            .Add(panel => panel.TransportOrderNumber, Number)
            .Add(panel => panel.OnClosed, EventCallback.Empty).Add(panel => panel.OnDone, EventCallback.Factory.Create<string>(this, _ => { })));

        page.Find("fluent-text-area").Input("CMR by post");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Commands.Received(1).ReleaseWithoutProofOfDeliveryAsync(Number, "CMR by post", null, Arg.Any<CancellationToken>()));
    }
}
