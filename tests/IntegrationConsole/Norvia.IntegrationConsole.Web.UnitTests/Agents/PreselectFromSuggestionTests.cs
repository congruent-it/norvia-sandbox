using Ardalis.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Discard;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

/// <summary>A Suggestion of Resubmit or Discard pre-selects the message in the resubmit and discard flow. Nothing executes until the operator confirms.</summary>
public sealed class PreselectFromSuggestionTests : ConsoleWebContext
{
    private const string Entity = "carrier-tracking";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private static readonly AgentRun Run = new("integration-operations", "run-7");

    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();

    public PreselectFromSuggestionTests()
    {
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read, OperatorRoles.Resubmit).SetPolicies(OperatorSignIn.ReadPolicy, OperatorSignIn.ResubmitPolicy);
        TurnAgentsOn();
        var message = new RecordedDeadLetter(3, Now.AddHours(-1), "UnmappableTrackingMessage", "DLV", 1, "corr-3", []);
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>()).Returns(new DeadLetteredMessageList(Entity, [message], HasMore: false));
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RecordedDeadLetterBody(message, "{}", new Dictionary<string, string>()));
        _deadLetters.ResubmitAsync(Arg.Any<ResubmitDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ResubmittedDeadLetter(Entity, "original-1", "copy-1", "corr-3", []));
        _deadLetters.DiscardAsync(Arg.Any<DiscardDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new DiscardedDeadLetter(Entity, "original-1", "corr-3", []));
    }

    [Fact]
    public void ASuggestionToResubmit_OffersToReview_ThenPreselectsTheMessage_WithoutSendingAnything()
    {
        Suggests(SuggestedActionKind.Resubmit);
        var page = Explained();

        page.Find("[data-act-on-suggestion=resubmit]").Click();

        var confirmation = page.Find("[aria-label='Message 3'] [role=alertdialog]");
        confirmation.TextContent.Should().Contain("The assistant suggested this").And.Contain("Resubmit message 3 to carrier-tracking");
        _deadLetters.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.ResubmitAsync));
    }

    [Fact]
    public void ASuggestionToResubmit_ThatTheOperatorConfirms_SendsTheSameCommandAsTheirOwnClick_CarryingTheAgentRun()
    {
        Suggests(SuggestedActionKind.Resubmit);
        var page = Explained();
        page.Find("[data-act-on-suggestion=resubmit]").Click();

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Confirm resubmit").Click();

        var command = ResubmitCommands().Should().ContainSingle().Subject;
        (command.Entity, command.SequenceNumber, command.ActingOn).Should().Be((Entity, 3L, Run));
        command.Caller.Identity!.Name.Should().Be("Iris Operator");
    }

    [Fact]
    public void ASuggestionToDiscard_Preselects_ButStillAsksForAReason_AndSendsNothingWithoutIt()
    {
        Suggests(SuggestedActionKind.Discard);
        var page = Explained();
        page.Find("[data-act-on-suggestion=discard]").Click();

        page.Find("[aria-label='Message 3'] [role=alertdialog]").TextContent.Should().Contain("The assistant suggested this").And.Contain("Discard message 3");
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Confirm discard").Click();

        page.Find("[data-reason-missing]").Should().NotBeNull();
        _deadLetters.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.DiscardAsync));
    }

    [Fact]
    public void ASuggestionToDiscard_ThatTheOperatorConfirmsWithAReason_CarriesTheAgentRun()
    {
        Suggests(SuggestedActionKind.Discard);
        var page = Explained();
        page.Find("[data-act-on-suggestion=discard]").Click();
        page.Find("fluent-text-area").Change("Duplicate of the Carrier's resend");

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Confirm discard").Click();

        var command = _deadLetters.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.DiscardAsync))
            .Select(call => (DiscardDeadLetteredMessageCommand)call.GetArguments()[0]!).Should().ContainSingle().Subject;
        (command.SequenceNumber, command.Reason, command.ActingOn).Should().Be((3L, "Duplicate of the Carrier's resend", Run));
    }

    [Fact]
    public void ASuggestionTheOperatorCancels_IsForgotten_SoTheirOwnResubmitCarriesNoAgent()
    {
        Suggests(SuggestedActionKind.Resubmit);
        var page = Explained();
        page.Find("[data-act-on-suggestion=resubmit]").Click();
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Cancel").Click();

        page.Find("[data-resubmit]").Click();
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Confirm resubmit").Click();

        ResubmitCommands().Should().ContainSingle().Which.ActingOn.Should().BeNull();
    }

    [Theory]
    [InlineData(SuggestedActionKind.FixTheCauseFirst)]
    [InlineData(SuggestedActionKind.NeedsAPerson)]
    public void ASuggestionToFixTheCauseOrCallAPerson_OffersNoWayToAct_AndNamesTheAction(SuggestedActionKind action)
    {
        Suggests(action);

        var page = Explained();

        page.FindAll("[data-act-on-suggestion]").Should().BeEmpty();
        page.Find("[data-testid=action-kind]").TextContent.Should().Be(action == SuggestedActionKind.FixTheCauseFirst ? "Fix the cause first" : "Needs a person");
    }

    [Fact]
    public void ASuggestionWithoutARun_OffersNoWayToAct_ForTheAuditCouldNotNameIt()
    {
        Explain.ExplainAsync(Arg.Any<ExplainDeadLetterQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Answer(SuggestedActionKind.Resubmit) with { Run = null }));

        Explained().FindAll("[data-act-on-suggestion]").Should().BeEmpty();
    }

    [Fact]
    public void ASuggestionToResubmit_ForAnOperatorWithoutTheResubmitRole_OffersNoWayToAct()
    {
        Suggests(SuggestedActionKind.Resubmit);
        Operator.SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);

        var page = Explained();

        page.Find("[data-testid=action-kind]").TextContent.Should().Be("Resubmit");
        page.FindAll("[data-act-on-suggestion]").Should().BeEmpty();
    }

    private static Suggestion Answer(SuggestedActionKind action) =>
        new("The map did not know DLV.", [new Evidence("A fact.", "r1", "peek_dead_letters")], "Do this.", Confidence.High, action, Run);

    private void Suggests(SuggestedActionKind action) =>
        Explain.ExplainAsync(Arg.Any<ExplainDeadLetterQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Answer(action)));

    private IRenderedComponent<DeadLetterMessages> Explained()
    {
        var page = Render<DeadLetterMessages>(parameters => parameters.Add(component => component.Entity, Entity));
        page.FindAll("tbody tr fluent-button").Single(button => button.TextContent.Trim() == "Explain").Click();
        return page;
    }

    private List<ResubmitDeadLetteredMessageCommand> ResubmitCommands() =>
        [.. _deadLetters.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.ResubmitAsync))
            .Select(call => (ResubmitDeadLetteredMessageCommand)call.GetArguments()[0]!)];
}
