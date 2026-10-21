using Ardalis.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Bulk;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests;

/// <summary>An operator with the Resubmit role resubmits or discards several dead-lettered messages of one queue at once.</summary>
public sealed class BulkDeadLetteredMessagesTests : ConsoleWebContext
{
    private const string Queue = "carrier-tracking";

    private static readonly DateTimeOffset Enqueued = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();
    private readonly FakeLogger<DeadLetterMessages> _log = new();

    public BulkDeadLetteredMessagesTests()
    {
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<ILogger<DeadLetterMessages>>(_log);
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read, OperatorRoles.Resubmit).SetPolicies(OperatorSignIn.ReadPolicy, OperatorSignIn.ResubmitPolicy);
        _deadLetters.ListMessagesAsync(new ListDeadLetteredMessagesQuery(Queue), Arg.Any<CancellationToken>())
            .Returns(new DeadLetteredMessageList(Queue, [Listed(3), Listed(4), Listed(5)], HasMore: false));
    }

    [Fact]
    public void Page_ForAnOperatorWithoutTheResubmitRole_OffersNoSelection()
    {
        Operator.SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);

        var page = RenderPage();

        page.FindAll("[data-select]").Should().BeEmpty();
        page.FindAll("[data-selection]").Should().BeEmpty();
    }

    [Fact]
    public void Select_OffersToResubmitOrDiscardTheSelection_AndSendsNothingYet()
    {
        var page = RenderPage();

        Tick(page, 3, 5);

        page.Find("[data-selected-count]").TextContent.Should().Contain("2 selected");
        page.Find("[data-resubmit-selected]").Should().NotBeNull();
        page.Find("[data-discard-selected]").Should().NotBeNull();
        _deadLetters.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Should().NotContain([nameof(IDeadLetterRequests.ResubmitManyAsync), nameof(IDeadLetterRequests.DiscardManyAsync)]);
    }

    [Fact]
    public void Resubmit_ShowsEachMessagesOutcome_AndLeavesTheFailedOnTheList()
    {
        _deadLetters.ResubmitManyAsync(Arg.Any<ResubmitDeadLetteredMessagesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BulkOutcome(Queue, [
                new MessageOutcome(3, MessageOutcomeStatus.Done),
                new MessageOutcome(4, MessageOutcomeStatus.Failed, "It is still on the dead-letter queue; try again in a moment."),
                new MessageOutcome(5, MessageOutcomeStatus.Gone)]));
        var page = RenderPage();
        Tick(page, 5, 4, 3);

        page.Find("[data-resubmit-selected]").Click();
        Button(page, "Confirm resubmit of selection").Click();

        page.Find("[data-message-outcome='3']").TextContent.Should().Contain("done");
        page.Find("[data-message-outcome='4']").TextContent.Should().Contain("failed").And.Contain("still on the dead-letter queue");
        page.Find("[data-message-outcome='5']").TextContent.Should().Contain("no longer on the queue");
        page.Find("[data-bulk-outcome] h2").TextContent.Should().Contain("1 failed");
        page.FindAll("tbody tr").Should().HaveCount(1);
        var command = _deadLetters.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.ResubmitManyAsync)).GetArguments()[0].Should().BeOfType<ResubmitDeadLetteredMessagesCommand>().Subject;
        command.Messages.Select(message => message.SequenceNumber).Should().BeEquivalentTo([3L, 4L, 5L]);
        command.Messages.Should().OnlyContain(message => message.Entity == Queue);
    }

    [Fact]
    public void Discard_AsksForOneReasonFirst_ThenSendsItWithTheSelection()
    {
        _deadLetters.DiscardManyAsync(Arg.Any<DiscardDeadLetteredMessagesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BulkOutcome(Queue, [new MessageOutcome(3, MessageOutcomeStatus.Done), new MessageOutcome(4, MessageOutcomeStatus.Done)]));
        var page = RenderPage();
        Tick(page, 3, 4);
        page.Find("[data-discard-selected]").Click();

        Button(page, "Confirm discard of selection").Click();

        page.Find("[data-reason-missing]").Should().NotBeNull();
        _deadLetters.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Should().NotContain(nameof(IDeadLetterRequests.DiscardManyAsync));

        page.Find("fluent-text-area").Input("ERP was down");
        Button(page, "Confirm discard of selection").Click();

        page.FindAll("[data-outcome=done]").Should().HaveCount(2);
        var command = _deadLetters.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.DiscardManyAsync)).GetArguments()[0].Should().BeOfType<DiscardDeadLetteredMessagesCommand>().Subject;
        command.Reason.Should().Be("ERP was down");
    }

    [Fact]
    public void Resubmit_WhenTheOperatorIsRefused_SaysNothingWasChanged()
    {
        _deadLetters.ResubmitManyAsync(Arg.Any<ResubmitDeadLetteredMessagesCommand>(), Arg.Any<CancellationToken>()).Returns(Result<BulkOutcome>.Forbidden());
        var page = RenderPage();
        Tick(page, 3);
        page.Find("[data-resubmit-selected]").Click();

        Button(page, "Confirm resubmit of selection").Click();

        page.Find("[role=status]").TextContent.Should().Contain("aren't allowed").And.Contain("Nothing was changed");
    }

    [Fact]
    public void Resubmit_WhenTheCallFails_SaysSo_WithoutTheException_AndLogsIt()
    {
        _deadLetters.ResubmitManyAsync(Arg.Any<ResubmitDeadLetteredMessagesCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result<BulkOutcome>>(_ => throw new HttpRequestException("403 AuthorizationFailed"));
        var page = RenderPage();
        Tick(page, 3);
        page.Find("[data-resubmit-selected]").Click();

        Button(page, "Confirm resubmit of selection").Click();

        page.Find("[role=status]").TextContent.Should().Contain("couldn't be sent");
        page.Markup.Should().NotContain("AuthorizationFailed");
        page.FindAll("tbody tr").Should().HaveCount(3);
        _log.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == LogLevel.Error && record.Message.Contains(Queue));
    }

    [Fact]
    public void Resubmit_WhenTheUseCaseRefusesTheSelection_ShowsWhy()
    {
        _deadLetters.ResubmitManyAsync(Arg.Any<ResubmitDeadLetteredMessagesCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<BulkOutcome>.Invalid(new ValidationError("messages", "A selection must be on one queue or subscription.")));
        var page = RenderPage();
        Tick(page, 3);
        page.Find("[data-resubmit-selected]").Click();

        Button(page, "Confirm resubmit of selection").Click();

        page.Find("[role=status]").TextContent.Should().Contain("one queue or subscription");
    }

    [Fact]
    public void Cancel_SendsNothing_AndKeepsTheSelection()
    {
        var page = RenderPage();
        Tick(page, 3);
        page.Find("[data-resubmit-selected]").Click();

        Button(page, "Cancel").Click();

        page.Find("[data-selected-count]").TextContent.Should().Contain("1 selected");
        _deadLetters.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Should().NotContain(nameof(IDeadLetterRequests.ResubmitManyAsync));
    }

    // Waits: the dialog renders after the click's async hop, not within it.
    private static AngleSharp.Dom.IElement Button(IRenderedComponent<DeadLetterMessages> page, string text)
    {
        AngleSharp.Dom.IElement? found = null;
        page.WaitForAssertion(() => found = page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text));
        return found!;
    }

    private IRenderedComponent<DeadLetterMessages> RenderPage() =>
        Render<DeadLetterMessages>(parameters => parameters.Add(component => component.Entity, Queue));

    private static void Tick(IRenderedComponent<DeadLetterMessages> page, params long[] sequenceNumbers)
    {
        foreach (var sequenceNumber in sequenceNumbers)
        {
            // The checkbox hands its change to the page asynchronously, so wait for the page to take it.
            var checkbox = page.Find($"fluent-checkbox[data-select='{sequenceNumber}']");
            checkbox.TriggerEvent("oncheckedchange", new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });
        }

        page.WaitForAssertion(() => page.Find("[data-selected-count]").TextContent.Should().Contain($"{sequenceNumbers.Length} selected"));
    }

    private static RecordedDeadLetter Listed(long sequenceNumber) =>
        new(sequenceNumber, Enqueued.AddMinutes(sequenceNumber), "MappingFailed", null, 2, $"corr-{sequenceNumber}", []);
}
