using Ardalis.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;

namespace Norvia.IntegrationConsole.Web.UnitTests;

/// <summary>An operator with the Resubmit role sends a dead-lettered queue message back, after confirming it.</summary>
public sealed class ResubmitDeadLetteredMessageTests : ConsoleWebContext
{
    private const string Queue = "carrier-tracking";

    private static readonly DateTimeOffset Enqueued = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterRequests _deadLetters = Substitute.For<IDeadLetterRequests>();
    private readonly FakeLogger<DeadLetterMessages> _log = new();

    public ResubmitDeadLetteredMessageTests()
    {
        Services.AddSingleton(_deadLetters);
        Services.AddSingleton<ILogger<DeadLetterMessages>>(_log);
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        Operator.SetAuthorized("Iris Operator").SetRoles(OperatorRoles.Read, OperatorRoles.Resubmit).SetPolicies(OperatorSignIn.ReadPolicy, OperatorSignIn.ResubmitPolicy);
        Lists(Queue, Listed(3), Listed(4));
        _deadLetters.OpenMessageAsync(Arg.Any<OpenDeadLetteredMessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => new RecordedDeadLetterBody(Listed(call.Arg<OpenDeadLetteredMessageQuery>().SequenceNumber), """{"consignee":{"name":"Café De Kroon"}}""", new Dictionary<string, string>()));
    }

    [Fact]
    public void Open_ForAnOperatorWithTheResubmitRole_OffersToResubmit_WithNoWayToEditTheBody()
    {
        var page = OpenedMessage(3);

        page.Find("[aria-label='Message 3'] [data-resubmit]").TextContent.Should().Contain("Resubmit");
        page.FindAll("[aria-label='Message 3'] textarea, [aria-label='Message 3'] input, [aria-label='Message 3'] fluent-text-area, [aria-label='Message 3'] fluent-text-field")
            .Should().BeEmpty();
    }

    [Fact]
    public void Open_ForAnOperatorWithoutTheResubmitRole_HidesTheControl()
    {
        Operator.SetRoles(OperatorRoles.Read).SetPolicies(OperatorSignIn.ReadPolicy);

        var page = OpenedMessage(3);

        page.Find("[aria-label='Message 3'] pre").Should().NotBeNull();
        page.FindAll("[data-resubmit]").Should().BeEmpty();
        page.Markup.Should().NotContain("Resubmit");
    }

    [Fact]
    public void Open_ForAMessageOnASubscription_OffersToResubmit_ToThatSubscriptionOnly()
    {
        Lists("shipment-events/carrier-booking", Listed(3));

        var page = OpenedMessage(3, "shipment-events/carrier-booking");
        page.Find("[data-resubmit]").Click();

        page.Find("[role=alertdialog]").TextContent.Should().Contain("to shipment-events/carrier-booking").And.Contain("to that subscription only");
    }

    [Fact]
    public void Resubmit_BeforeItIsConfirmed_AsksFirst_AndSendsNothing()
    {
        var page = OpenedMessage(3);

        page.Find("[data-resubmit]").Click();

        var confirmation = page.Find("[role=alertdialog]");
        confirmation.TextContent.Should().Contain("Resubmit message 3 to carrier-tracking").And.Contain("Your name is recorded");
        page.FindAll("[data-resubmit]").Should().BeEmpty();
        SentCommands().Should().BeEmpty();
    }

    [Fact]
    public void Resubmit_WhenTheOperatorCancels_SendsNothing_AndOffersItAgain()
    {
        var page = OpenedMessage(3);
        page.Find("[data-resubmit]").Click();

        Button(page, "Cancel").Click();

        page.FindAll("[role=alertdialog]").Should().BeEmpty();
        page.Find("[data-resubmit]").Should().NotBeNull();
        SentCommands().Should().BeEmpty();
    }

    [Fact]
    public void Resubmit_WhenConfirmed_SendsTheMessageAsTheOperator_ReportsTheOutcome_AndLeavesTheList()
    {
        _deadLetters.ResubmitAsync(Arg.Any<ResubmitDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ResubmittedDeadLetter(Queue, "original-1", "copy-9", "corr-1", []));
        var page = OpenedMessage(3);
        page.Find("[data-resubmit]").Click();

        Button(page, "Confirm resubmit").Click();

        var command = SentCommands().Should().ContainSingle().Subject;
        (command.Entity, command.SequenceNumber).Should().Be((Queue, 3L));
        command.Caller.Identity!.Name.Should().Be("Iris Operator");
        command.Caller.IsInRole(OperatorRoles.Resubmit).Should().BeTrue();
        page.Find("[role=status]").TextContent.Should().Contain("Resubmitted message 3 to carrier-tracking").And.Contain("copy-9");
        page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("corr-4");
        page.FindAll("section[aria-label^='Message']").Should().BeEmpty();
    }

    [Fact]
    public void Resubmit_WhenTheMessageIsGone_SaysSo_AndLeavesTheList()
    {
        _deadLetters.ResubmitAsync(Arg.Any<ResubmitDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Result<ResubmittedDeadLetter>.NotFound());
        var page = OpenedMessage(3);
        page.Find("[data-resubmit]").Click();

        Button(page, "Confirm resubmit").Click();

        page.Find("[role=status]").TextContent.Should().Contain("Message 3 is no longer on the queue");
        page.FindAll("tbody tr").Should().ContainSingle();
    }

    [Fact]
    public void Resubmit_WhenTheUseCaseRefusesTheOperator_SaysNothingWasSent_AndKeepsTheMessage()
    {
        _deadLetters.ResubmitAsync(Arg.Any<ResubmitDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Result<ResubmittedDeadLetter>.Forbidden());
        var page = OpenedMessage(3);
        page.Find("[data-resubmit]").Click();

        Button(page, "Confirm resubmit").Click();

        page.Find("[role=status]").TextContent.Should().Contain("aren't allowed to resubmit").And.Contain("Nothing was sent");
        page.FindAll("tbody tr").Should().HaveCount(2);
        page.FindAll("[role=alertdialog]").Should().BeEmpty();
    }

    [Fact]
    public void Resubmit_WhenTheRequestIsInvalid_SaysWhy()
    {
        _deadLetters.ResubmitAsync(Arg.Any<ResubmitDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResubmittedDeadLetter>.Invalid(new ValidationError("Entity", "Resubmit a message by its queue and its sequence number.")));
        var page = OpenedMessage(3);
        page.Find("[data-resubmit]").Click();

        Button(page, "Confirm resubmit").Click();

        page.Find("[role=status]").TextContent.Should().Contain("Resubmit a message by its queue and its sequence number.");
    }

    [Fact]
    public void Resubmit_WhenTheSendFails_SaysItIsStillOnTheQueue_WithoutTheReason_AndLogsIt()
    {
        _deadLetters.ResubmitAsync(Arg.Any<ResubmitDeadLetteredMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result<ResubmittedDeadLetter>>(_ => throw new HttpRequestException("403 AuthorizationFailed"));
        var page = OpenedMessage(3);
        page.Find("[data-resubmit]").Click();

        Button(page, "Confirm resubmit").Click();

        page.Find("[role=status]").TextContent.Should().Contain("couldn't be resubmitted").And.Contain("still on the dead-letter queue");
        page.Markup.Should().NotContain("AuthorizationFailed");
        page.FindAll("tbody tr").Should().HaveCount(2);
        _log.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == LogLevel.Error && record.Message.Contains(Queue) && !record.Message.Contains("Café"));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<DeadLetterMessages> page, string text) =>
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private IRenderedComponent<DeadLetterMessages> OpenedMessage(long sequenceNumber, string entity = Queue)
    {
        var page = Render<DeadLetterMessages>(parameters => parameters.Add(component => component.Entity, entity));
        page.Find($"fluent-button[data-message='{sequenceNumber}']").Click();
        return page;
    }

    private List<ResubmitDeadLetteredMessageCommand> SentCommands() =>
        [.. _deadLetters.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IDeadLetterRequests.ResubmitAsync))
            .Select(call => (ResubmitDeadLetteredMessageCommand)call.GetArguments()[0]!)];

    private void Lists(string entity, params RecordedDeadLetter[] messages) =>
        _deadLetters.ListMessagesAsync(new ListDeadLetteredMessagesQuery(entity), Arg.Any<CancellationToken>())
            .Returns(new DeadLetteredMessageList(entity, messages, HasMore: false));

    private static RecordedDeadLetter Listed(long sequenceNumber) =>
        new(sequenceNumber, Enqueued.AddMinutes(sequenceNumber), "MappingFailed", null, 2, $"corr-{sequenceNumber}", []);
}
