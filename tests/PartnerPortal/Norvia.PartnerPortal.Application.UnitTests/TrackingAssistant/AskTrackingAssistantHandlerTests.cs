using System.Diagnostics;
using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Application.UnitTests.TrackingAssistant;

public sealed class AskTrackingAssistantHandlerTests : IDisposable
{
    private static readonly AssistantReply GoodReply = new(
        "TO-0000042 is In transit. The ETA is 02-10-2030 11:30 (last update 01-10-2030 14:05).",
        [new AssistantClaim("detail.status"), new AssistantClaim("detail.eta"), new AssistantClaim("detail.lastUpdate")], NoInformation: false, TokensUsed: 700);

    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly INorviaPartnerApi _api = Substitute.For<INorviaPartnerApi>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();
    private readonly IAssistantRunIds _runIds = Substitute.For<IAssistantRunIds>();
    private readonly FakeTimeProvider _time = new(TrackingFixtures.Now);
    private readonly AssistantActivities _activities = new("test-portal");
    private readonly AssistantLimits _limits = new();
    private readonly AssistantLimiter _limiter;
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;

    public AskTrackingAssistantHandlerTests()
    {
        _limiter = new AssistantLimiter(_limits, _time);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _runIds.NewRunId().Returns("run-1");
        _api.GetTransportOrderAsync(Samples.BrouwerDranken, "TO-0000042", Arg.Any<CancellationToken>()).Returns(TrackingAnswer.Found(TrackingFixtures.Detail, "corr-detail"));
        _api.ListTransportOrdersAsync(Samples.BrouwerDranken, Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Found<IReadOnlyList<ShipperTrackingSummary>>([TrackingFixtures.Summary("TO-0000042", "BD-1")], "corr-list"));
        _agent.AskAsync(Arg.Any<AssistantRequest>(), Arg.Any<CancellationToken>()).Returns(Result<AssistantReply>.Success(GoodReply));
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "test-portal",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
    }

    private AskTrackingAssistantHandler Handler() => new(_personas, _api, _agent, _limiter, _limits, _runIds, _activities, _time, NullLogger<AskTrackingAssistantHandler>.Instance);

    private ValueTask<AssistantAnswer> Ask(string question = "Where is my order?", string? open = "TO-0000042", Norvia.PartnerPortal.Domain.Personas.PersonaId? persona = null) =>
        Handler().Handle(new AskTrackingAssistantCommand(persona ?? Samples.BrouwerDranken.Id, question, [], open), CancellationToken.None);

    [Fact]
    public async Task Handle_AnswersFromTheFetchedData()
    {
        var answer = await Ask();

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Answered, GoodReply.Answer, null, "run-1"));
    }

    [Fact]
    public async Task Handle_GivesTheModelOnlyThePersonasOwnOrders_FetchedAsThePersona()
    {
        await Ask();

        await _agent.Received(1).AskAsync(
            Arg.Is<AssistantRequest>(request => request.Input.Text.Contains("TO-0000042") && request.MaxOutputTokens == 400), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().GetTransportOrderAsync(Arg.Is<Norvia.PartnerPortal.Domain.Personas.Persona>(p => p != Samples.BrouwerDranken), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MakesAtMostThreeCalls_FindingANamedReferenceOnce()
    {
        _api.FindTransportOrderAsync(Samples.BrouwerDranken, "SH-2030-0142", Arg.Any<CancellationToken>()).Returns(TrackingAnswer.Found(TrackingFixtures.Detail with { TransportOrderNumber = "TO-0000077" }, "corr-find"));

        await Ask("Where is SH-2030-0142 and BD-2030-0999?");

        await _api.Received(1).GetTransportOrderAsync(Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.Received(1).ListTransportOrdersAsync(Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>());
        await _api.Received(1).FindTransportOrderAsync(Arg.Any<Norvia.PartnerPortal.Domain.Personas.Persona>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _agent.Received(1).AskAsync(Arg.Is<AssistantRequest>(request => request.Input.Text.Contains("TO-0000077")), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TrackingOutcome.Unavailable)]
    [InlineData(TrackingOutcome.Refused)]
    public async Task Handle_WhenTrackingFails_MakesNoModelCall_AndSaysSo(TrackingOutcome outcome)
    {
        _api.ListTransportOrdersAsync(Samples.BrouwerDranken, Arg.Any<ShipperTrackingFilter>(), Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Failed<IReadOnlyList<ShipperTrackingSummary>>(outcome, "down", "corr-list"));

        var answer = await Ask();

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Failed, "I can't reach tracking right now", "TmsUnavailable", "run-1"));
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_OverTheRateLimit_MakesNoModelCall_AndIsRefused()
    {
        for (var i = 0; i < 6; i++)
        {
            await Ask();
        }

        _agent.ClearReceivedCalls();
        var answer = await Ask();

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Refused, "You have asked a lot of questions. Try again in a few minutes.", "AgentRateLimited", "run-1"));
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ADutchQuestionOverTheLimit_GetsTheDutchText()
    {
        var answer = await Ask(new string('x', 501) + " waar is mijn bestelling");

        answer.Text.Should().Be("U heeft veel vragen gesteld. Probeer het over enkele minuten opnieuw.");
    }

    [Fact]
    public async Task Handle_WhenTheEstateBudgetIsUsedUp_SaysUnavailable()
    {
        _limiter.Record(Samples.BrouwerDranken.Id, 500_000);

        var answer = await Ask();

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Refused, "Assistant unavailable", "AgentUnavailable", "run-1"));
    }

    [Fact]
    public async Task Handle_WhenTheModelIsDown_SaysUnavailable_InTheQuestionsLanguage()
    {
        _agent.AskAsync(Arg.Any<AssistantRequest>(), Arg.Any<CancellationToken>()).Returns(Result<AssistantReply>.Unavailable("AgentUnavailable"));

        var answer = await Ask("Waar is mijn bestelling?");

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Failed, "Assistent niet beschikbaar", "AgentUnavailable", "run-1"));
    }

    [Fact]
    public async Task Handle_WhenTheAnswerFailsTheCheck_ShowsOnlyTheFixedText_AndStillCountsTheTokens()
    {
        _agent.AskAsync(Arg.Any<AssistantRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AssistantReply>.Success(new AssistantReply("It arrives 05-10-2030 10:00.", [new AssistantClaim("detail.eta")], false, 49_999)));

        var answer = await Ask();

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Failed, "I have no information about that.", "AgentAnswerRejected", "run-1"));
        _limiter.Admit(Samples.BrouwerDranken.Id, 1).Should().Be(Admission.Allowed);
        _limiter.Record(Samples.BrouwerDranken.Id, 1);
        _limiter.Admit(Samples.BrouwerDranken.Id, 1).Should().Be(Admission.RateLimited);
    }

    [Fact]
    public async Task Handle_ForANoInformationReply_ShowsTheFixedText_AsAnswered()
    {
        _agent.AskAsync(Arg.Any<AssistantRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AssistantReply>.Success(new AssistantReply("whatever", [], NoInformation: true, 5)));

        var answer = await Ask("Waarom is het te laat?");

        answer.Should().Be(new AssistantAnswer(AssistantOutcome.Answered, "Daar heb ik geen informatie over.", null, "run-1"));
    }

    [Fact]
    public async Task Handle_ForACarrierPersona_MakesNoCall()
    {
        var answer = await Ask(persona: Samples.VanDijkTransport.Id);

        answer.Outcome.Should().Be(AssistantOutcome.Refused);
        await _api.DidNotReceiveWithAnyArgs().ListTransportOrdersAsync(default!, default!, TestContext.Current.CancellationToken);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_RecordsASpanWithTheKeysAndNeitherTheQuestionNorTheAnswer()
    {
        await Ask("Where is my order?");

        var span = _spans.Should().ContainSingle().Subject;
        span.GetTagItem("partyId").Should().Be("brouwer-dranken");
        span.GetTagItem("runId").Should().Be("run-1");
        span.GetTagItem("outcome").Should().Be("Answered");
        span.GetTagItem("agent.tokens").Should().Be(700);
        span.GetTagItem("fetch.correlationIds").Should().Be("corr-detail,corr-list");
        span.TagObjects.Select(tag => tag.Value?.ToString()).Should().NotContain(value => value!.Contains("Where is my order") || value.Contains("ETA"));
    }

    [Fact]
    public async Task Handle_ASpanOfAFailedRun_CarriesTheFailureCode()
    {
        _agent.AskAsync(Arg.Any<AssistantRequest>(), Arg.Any<CancellationToken>()).Returns(Result<AssistantReply>.Unavailable("AgentUnavailable"));

        await Ask();

        var span = _spans.Should().ContainSingle().Subject;
        span.GetTagItem("outcome").Should().Be("Failed");
        span.GetTagItem("failureCode").Should().Be("AgentUnavailable");
    }
}
