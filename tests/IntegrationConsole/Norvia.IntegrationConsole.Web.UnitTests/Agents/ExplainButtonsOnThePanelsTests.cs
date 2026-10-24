using Ardalis.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Failed;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Runs;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

/// <summary>The Explain buttons of the Failed runs and Dead-letter queues panels, and the "what needs attention" summary.</summary>
public sealed class ExplainButtonsOnThePanelsTests : ConsoleWebContext
{
    private const string Entity = "shipment-events/carrier-tracking";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private static readonly Suggestion Answer = new("Why it failed.", [new Evidence("A fact.", "r1", "peek_dead_letters")], "Do this.", Confidence.High);

    private readonly IRunRequests _runs = Substitute.For<IRunRequests>();
    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();

    public ExplainButtonsOnThePanelsTests()
    {
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);
        Services.AddSingleton(_runs);
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        _runs.ListFailedAsync(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>()).Returns(new FailedRunList(
            Now.AddHours(-24),
            Now,
            ["transport-order-intake"],
            [new FailedRun("transport-order-intake", new WorkflowRun("logic-intake", "transport-order-intake", "run-1"), Now.AddMinutes(-5), "corr-1", "Submit", new StepError("Unavailable", "503"))]));
        _deadLetters.ListAsync(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new DeadLetterQueueList(Now, [new DeadLetterEntity(Entity, 2, TimeSpan.FromHours(1))]));
        _deadLetters.ListMessagesAsync(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>()).Returns(new DeadLetteredMessageList(
            Entity,
            [new RecordedDeadLetter(4, Now.AddHours(-1), "UnmappableTrackingMessage", "DLV", 1, "corr-4", [])],
            HasMore: false));
        Explain.ExplainAsync(Arg.Any<ExplainFailedRunQuery>(), Arg.Any<CancellationToken>()).Returns(Answer);
        Explain.ExplainAsync(Arg.Any<ExplainDeadLetterQuery>(), Arg.Any<CancellationToken>()).Returns(Answer);
        Explain.SummariseAsync(Arg.Any<SummariseDeadLettersQuery>(), Arg.Any<CancellationToken>()).Returns(Answer);
    }

    [Fact]
    public void FailedRuns_WhileTheAgentsAreOff_HasNoAssistantColumn()
    {
        var page = Render<FailedRuns>();

        page.FindAll("fluent-button").Should().BeEmpty();
        page.Markup.Should().NotContain("Assistant");
    }

    [Fact]
    public void FailedRuns_WhileTheAgentsAreOn_ExplainsThatRun_FromItsFlowStartAndTrail()
    {
        TurnAgentsOn();
        var page = Render<FailedRuns>();

        page.Find("tbody tr fluent-button").TextContent.Trim().Should().Be("Explain");
        page.Find("tbody tr fluent-button").Click();

        Explain.Received(1).ExplainAsync(new ExplainFailedRunQuery("transport-order-intake", Now.AddMinutes(-5), "corr-1"), Arg.Any<CancellationToken>());
        page.Find("tbody tr [data-testid=explanation]").TextContent.Should().Contain("Why it failed.").And.Contain("Do this.");
    }

    [Fact]
    public void DeadLetterMessages_WhileTheAgentsAreOff_HasNoAssistantColumn()
    {
        var page = Render<DeadLetterMessages>(parameters => parameters.Add(component => component.Entity, Entity));

        page.FindAll("tbody fluent-button").Select(button => button.TextContent.Trim()).Should().NotContain("Explain");
        page.Markup.Should().NotContain("Assistant");
    }

    [Fact]
    public void DeadLetterMessages_WhileTheAgentsAreOn_ExplainsThatMessage_ByQueueAndSequenceNumber()
    {
        TurnAgentsOn();
        var page = Render<DeadLetterMessages>(parameters => parameters.Add(component => component.Entity, Entity));

        page.FindAll("tbody tr fluent-button").Single(button => button.TextContent.Trim() == "Explain").Click();

        Explain.Received(1).ExplainAsync(new ExplainDeadLetterQuery(Entity, 4, "corr-4"), Arg.Any<CancellationToken>());
        page.Find("tbody tr [data-testid=explanation]").TextContent.Should().Contain("Why it failed.");
    }

    [Fact]
    public void DeadLetterQueues_WhileTheAgentsAreOff_OffersNoSummary()
    {
        var page = Render<DeadLetterQueues>();

        page.FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void DeadLetterQueues_WhileTheAgentsAreOn_SummarisesWhatNeedsAttention_WithAFixedQuestion()
    {
        TurnAgentsOn();
        var page = Render<DeadLetterQueues>();

        page.Find("section[aria-label='What needs attention'] fluent-button").TextContent.Trim().Should().Be("What needs attention");
        page.Find("section[aria-label='What needs attention'] fluent-button").Click();

        Explain.Received(1).SummariseAsync(new SummariseDeadLettersQuery(), Arg.Any<CancellationToken>());
        page.Find("section[aria-label='What needs attention'] [data-testid=explanation]").TextContent.Should().Contain("Why it failed.");
        page.FindAll("textarea, input[type=text]").Should().BeEmpty(because: "the question takes no free text");
    }
}
