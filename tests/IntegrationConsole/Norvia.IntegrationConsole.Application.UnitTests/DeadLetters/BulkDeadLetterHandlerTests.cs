using System.Security.Claims;
using Ardalis.Result;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Bulk;
using Norvia.IntegrationConsole.Application.DeadLetters.Discard;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Application.UnitTests.DeadLetters;

public sealed class BulkDeadLetterHandlerTests
{
    private const string Queue = "carrier-tracking";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly FakeTimeProvider _time = new(Now);

    public BulkDeadLetterHandlerTests()
    {
        _queues.ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => new ResubmittedDeadLetter(call.ArgAt<string>(0), $"original-{call.ArgAt<long>(1)}", "copy", null, []));
        _queues.DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => new DiscardedDeadLetter(call.ArgAt<string>(0), $"original-{call.ArgAt<long>(1)}", null, []));
    }

    [Fact]
    public async Task Resubmit_SendsTheSelectionInOriginalEnqueueOrder_WhateverOrderItWasSelectedIn()
    {
        var result = await ResubmitAsync(Selection(Queue, (7, 3), (5, 1), (6, 2)));

        result.Value.Messages.Select(message => message.SequenceNumber).Should().Equal(5, 6, 7);
        Received.InOrder(() =>
        {
            _queues.ResubmitAsync(Queue, 5, Arg.Any<CancellationToken>());
            _queues.ResubmitAsync(Queue, 6, Arg.Any<CancellationToken>());
            _queues.ResubmitAsync(Queue, 7, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Resubmit_OfMessagesEnqueuedAtTheSameTime_FallsBackToTheSequenceNumber_AndSendsADuplicateOnce()
    {
        var result = await ResubmitAsync(Selection(Queue, (9, 1), (8, 1), (8, 1)));

        result.Value.Messages.Select(message => message.SequenceNumber).Should().Equal(8, 9);
    }

    [Fact]
    public async Task Resubmit_WhenOneMessageFails_ReportsItFailed_AndTheOthersStillComplete()
    {
        _queues.ResubmitAsync(Queue, 6, Arg.Any<CancellationToken>()).Returns<ResubmittedDeadLetter?>(_ => throw new InvalidOperationException("broker said no"));

        var result = await ResubmitAsync(Selection(Queue, (5, 1), (6, 2), (7, 3)));

        result.Value.Messages.Select(message => message.Status).Should()
            .Equal(MessageOutcomeStatus.Done, MessageOutcomeStatus.Failed, MessageOutcomeStatus.Done);
        result.Value.Messages[1].Detail.Should().NotContain("broker said no");
        await _audit.Received(2).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resubmit_WritesOneAuditEventPerMessage()
    {
        await ResubmitAsync(Selection(Queue, (5, 1), (6, 2), (7, 3)));

        await _audit.Received(1).WriteAsync(Arg.Is<AuditEvent>(audit => audit.Action == AuditActions.Resubmit && audit.MessageId == "original-5"), Arg.Any<CancellationToken>());
        await _audit.Received(1).WriteAsync(Arg.Is<AuditEvent>(audit => audit.Action == AuditActions.Resubmit && audit.MessageId == "original-6"), Arg.Any<CancellationToken>());
        await _audit.Received(1).WriteAsync(Arg.Is<AuditEvent>(audit => audit.Action == AuditActions.Resubmit && audit.MessageId == "original-7"), Arg.Any<CancellationToken>());
        await _audit.Received(3).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resubmit_ForAMessageThatIsGone_ReportsItGone_AndAuditsNothingForIt()
    {
        _queues.ResubmitAsync(Queue, 6, Arg.Any<CancellationToken>()).Returns((ResubmittedDeadLetter?)null);

        var result = await ResubmitAsync(Selection(Queue, (5, 1), (6, 2)));

        result.Value.Messages.Select(message => message.Status).Should().Equal(MessageOutcomeStatus.Done, MessageOutcomeStatus.Gone);
        await _audit.Received(1).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resubmit_OfASelectionSpanningTwoEntities_IsRefused_AndSendsNothing()
    {
        var result = await ResubmitAsync([new DeadLetterSelection(Queue, 5, Now), new DeadLetterSelection("shipment-events/carrier-booking", 6, Now)]);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("one queue or subscription");
        await _queues.DidNotReceive().ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    public async Task Resubmit_OfAnEmptySelection_IsInvalid(int count)
    {
        var result = await ResubmitAsync(Selection(Queue).Take(count).ToList());

        result.Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task Resubmit_ForAnOperatorWithoutTheResubmitRole_IsForbidden_AndSendsNothing()
    {
        var result = await ResubmitAsync(Selection(Queue, (5, 1), (6, 2)), Operator(OperatorRoles.Read));

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discard_SendsTheSelectionInOriginalEnqueueOrder_AndRecordsTheReasonOnEachAuditEvent()
    {
        var result = await DiscardAsync(Selection(Queue, (7, 3), (5, 1), (6, 2)), " Duplicates ");

        result.Value.Messages.Select(message => message.SequenceNumber).Should().Equal(5, 6, 7);
        await _audit.Received(3).WriteAsync(Arg.Is<AuditEvent>(audit => audit.Action == AuditActions.Discard && audit.Reason == "Duplicates"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discard_WhenOneMessageFails_ReportsItFailed_AndTheOthersStillComplete()
    {
        _queues.DiscardAsync(Queue, 5, Arg.Any<CancellationToken>()).Returns<DiscardedDeadLetter?>(_ => throw new InvalidOperationException());

        var result = await DiscardAsync(Selection(Queue, (5, 1), (6, 2)), "Duplicates");

        result.Value.Messages.Select(message => message.Status).Should().Equal(MessageOutcomeStatus.Failed, MessageOutcomeStatus.Done);
        await _audit.Received(1).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discard_OfASelectionSpanningTwoEntities_IsRefused_AndDiscardsNothing()
    {
        var result = await DiscardAsync([new DeadLetterSelection(Queue, 5, Now), new DeadLetterSelection("other", 6, Now)], "Duplicates");

        result.Status.Should().Be(ResultStatus.Invalid);
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Discard_WithoutAReason_IsInvalid_AndDiscardsNothing(string reason)
    {
        var result = await DiscardAsync(Selection(Queue, (5, 1)), reason);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle(error => error.Identifier == nameof(DiscardDeadLetteredMessagesCommand.Reason));
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discard_ForAnOperatorWithoutTheResubmitRole_IsForbidden()
    {
        var result = await DiscardAsync(Selection(Queue, (5, 1)), "Duplicates", Operator(OperatorRoles.Read));

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    private static List<DeadLetterSelection> Selection(string entity, params (long Sequence, int Minute)[] messages) =>
        [.. messages.Select(message => new DeadLetterSelection(entity, message.Sequence, Now.AddMinutes(message.Minute)))];

    private static ClaimsPrincipal Operator(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Iris Operator"), new Claim(ClaimTypes.Role, role)], "test", ClaimTypes.Name, ClaimTypes.Role));

    private async Task<Result<BulkOutcome>> ResubmitAsync(IReadOnlyList<DeadLetterSelection> messages, ClaimsPrincipal? caller = null) =>
        await new ResubmitDeadLetteredMessagesHandler(new ResubmitDeadLetteredMessageHandler(_queues, _audit, _time))
            .Handle(new ResubmitDeadLetteredMessagesCommand(caller ?? Operator(OperatorRoles.Resubmit), messages), TestContext.Current.CancellationToken);

    private async Task<Result<BulkOutcome>> DiscardAsync(IReadOnlyList<DeadLetterSelection> messages, string reason, ClaimsPrincipal? caller = null) =>
        await new DiscardDeadLetteredMessagesHandler(new DiscardDeadLetteredMessageHandler(_queues, _audit, _time))
            .Handle(new DiscardDeadLetteredMessagesCommand(caller ?? Operator(OperatorRoles.Resubmit), messages, reason), TestContext.Current.CancellationToken);
}
