using Ardalis.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Discard;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests;

/// <summary>An operator with the Resubmit role discards a dead-lettered message, after confirming it with a reason.</summary>
public sealed class DiscardDeadLetteredMessageTests : ConsoleWebContext
{
    private const string Queue = "carrier-tracking";

    private static readonly DateTimeOffset Enqueued = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();
    private readonly FakeLogger<DeadLetterMessages> _log = new();

    public DiscardDeadLetteredMessageTests()
    {
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<ILogger<DeadLetterMessages>>(_log);
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read, OperatorRoles.Resubmit).SetPolicies(OperatorSignIn.ReadPolicy, OperatorSignIn.ResubmitPolicy);
        _deadLetters.ListMessagesAsync(new ListDeadLetteredMessagesQuery(Queue), Arg.Any<CancellationToken>())
            .Returns(new DeadLetteredMessageList(Queue, [Listed(3), Listed(4)], HasMore: false));
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => new RecordedDeadLetterBody(Listed(call.Arg<OpenDeadLetteredMessageQuery>().SequenceNumber), "{}", new Dictionary<string, string>()));
    }

    [Fact]
    public void Open_ForAnOperatorWithTheResubmitRole_OffersToDiscard()
    {
        var page = OpenedMessage(3);

        page.Find("[aria-label='Message 3'] [data-discard]").TextContent.Should().Contain("Discard");
    }

    [Fact]
    public void Open_ForAnOperatorWithoutTheResubmitRole_HidesTheControl()
    {
        Operator.SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);

        var page = OpenedMessage(3);

        page.Find("[aria-label='Message 3'] pre").Should().NotBeNull();
        page.FindAll("[data-discard]").Should().BeEmpty();
        page.Markup.Should().NotContain("Discard");
    }

    [Fact]
    public void Discard_BeforeItIsConfirmed_AsksForAReasonFirst_AndDiscardsNothing()
    {
        var page = OpenedMessage(3);

        page.Find("[data-discard]").Click();

        page.Find("[role=alertdialog]").TextContent.Should().Contain("Discard message 3 of carrier-tracking").And.Contain("Your name and the reason are recorded");
        page.Find("fluent-text-area[aria-label='Reason for discarding']").Should().NotBeNull();
        SentCommands().Should().BeEmpty();
    }

    [Fact]
    public void Discard_WhenTheOperatorCancels_DiscardsNothing_AndOffersItAgain()
    {
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();

        Button(page, "Cancel").Click();

        page.FindAll("[role=alertdialog]").Should().BeEmpty();
        page.Find("[data-discard]").Should().NotBeNull();
        SentCommands().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Discard_WithoutAReason_IsRefusedByTheForm_AndDiscardsNothing(string reason)
    {
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();
        page.Find("fluent-text-area").Change(reason);

        Button(page, "Confirm discard").Click();

        page.Find("[data-reason-missing]").TextContent.Should().Contain("Give a reason");
        page.Find("[role=alertdialog]").Should().NotBeNull();
        SentCommands().Should().BeEmpty();
        page.FindAll("tbody tr").Should().HaveCount(2);
    }

    [Fact]
    public void Discard_WhenConfirmedWithAReason_DiscardsAsTheOperator_ReportsTheOutcome_AndLeavesTheList()
    {
        _deadLetters.DiscardAsync(Arg.Any<DiscardDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new DiscardedDeadLetter(Queue, "original-1", "corr-3", []));
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();
        page.Find("fluent-text-area").Input("Duplicate of SH-0041");

        Button(page, "Confirm discard").Click();

        var command = SentCommands().Should().ContainSingle().Subject;
        (command.Entity, command.SequenceNumber, command.Reason).Should().Be((Queue, 3L, "Duplicate of SH-0041"));
        command.Caller.Identity!.Name.Should().Be("Iris Operator");
        command.Caller.IsInRole(OperatorRoles.Resubmit).Should().BeTrue();
        page.Find("[role=status]").TextContent.Should().Contain("Discarded message 3 of carrier-tracking");
        page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("corr-4");
        page.FindAll("section[aria-label^='Message']").Should().BeEmpty();
    }

    [Fact]
    public void Discard_WhenTheMessageIsGone_SaysSo_AndLeavesTheList()
    {
        _deadLetters.DiscardAsync(Arg.Any<DiscardDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Result<DiscardedDeadLetter>.NotFound());
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();
        page.Find("fluent-text-area").Input("Duplicate");

        Button(page, "Confirm discard").Click();

        page.Find("[role=status]").TextContent.Should().Contain("Message 3 is no longer on the queue");
        page.FindAll("tbody tr").Should().ContainSingle();
    }

    [Fact]
    public void Discard_WhenTheUseCaseRefusesTheOperator_SaysNothingWasDiscarded_AndKeepsTheMessage()
    {
        _deadLetters.DiscardAsync(Arg.Any<DiscardDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Result<DiscardedDeadLetter>.Forbidden());
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();
        page.Find("fluent-text-area").Input("Duplicate");

        Button(page, "Confirm discard").Click();

        page.Find("[role=status]").TextContent.Should().Contain("aren't allowed to discard").And.Contain("Nothing was discarded");
        page.FindAll("tbody tr").Should().HaveCount(2);
        page.FindAll("[role=alertdialog]").Should().BeEmpty();
    }

    [Fact]
    public void Discard_WhenTheRequestIsInvalid_SaysWhy()
    {
        _deadLetters.DiscardAsync(Arg.Any<DiscardDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<DiscardedDeadLetter>.Invalid(new ValidationError("Reason", "Give a reason for discarding the message.")));
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();
        page.Find("fluent-text-area").Input("Duplicate");

        Button(page, "Confirm discard").Click();

        page.Find("[role=status]").TextContent.Should().Contain("Give a reason for discarding the message.");
    }

    [Fact]
    public void Discard_WhenItFails_SaysItIsStillOnTheQueue_WithoutTheReason_AndLogsIt()
    {
        _deadLetters.DiscardAsync(Arg.Any<DiscardDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result<DiscardedDeadLetter>>(_ => throw new HttpRequestException("403 AuthorizationFailed"));
        var page = OpenedMessage(3);
        page.Find("[data-discard]").Click();
        page.Find("fluent-text-area").Input("Duplicate");

        Button(page, "Confirm discard").Click();

        page.Find("[role=status]").TextContent.Should().Contain("couldn't be discarded").And.Contain("still on the dead-letter queue");
        page.Markup.Should().NotContain("AuthorizationFailed");
        page.FindAll("tbody tr").Should().HaveCount(2);
        _log.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == LogLevel.Error && record.Message.Contains(Queue));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<DeadLetterMessages> page, string text) =>
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private IRenderedComponent<DeadLetterMessages> OpenedMessage(long sequenceNumber)
    {
        var page = Render<DeadLetterMessages>(parameters => parameters.Add(component => component.Entity, Queue));
        page.Find($"fluent-button[data-message='{sequenceNumber}']").Click();
        return page;
    }

    private List<DiscardDeadLetteredMessageCommand> SentCommands() =>
        [.. _deadLetters.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.DiscardAsync))
            .Select(call => (DiscardDeadLetteredMessageCommand)call.GetArguments()[0]!)];

    private static RecordedDeadLetter Listed(long sequenceNumber) =>
        new(sequenceNumber, Enqueued.AddMinutes(sequenceNumber), "MappingFailed", null, 2, $"corr-{sequenceNumber}", []);
}
