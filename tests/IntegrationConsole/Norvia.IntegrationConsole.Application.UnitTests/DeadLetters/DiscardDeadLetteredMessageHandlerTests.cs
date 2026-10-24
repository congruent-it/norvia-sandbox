using System.Security.Claims;
using Ardalis.Result;
using Microsoft.Extensions.Time.Testing;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Discard;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.UnitTests.DeadLetters;

public sealed class DiscardDeadLetteredMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly DiscardedDeadLetter Discarded =
        new("carrier-tracking", "original-1", "corr-1", [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042")]);

    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_ForAnOperatorWithTheResubmitRoleAndAReason_DiscardsTheMessage_AndReportsTheOutcome()
    {
        _queues.DiscardAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Discarded);

        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, "Duplicate of SH-0041");

        result.Value.Should().Be(Discarded);
    }

    [Fact]
    public async Task Handle_WhenTheMessageIsDiscarded_WritesOneAuditEvent_WithWhoWhatWhichMessageWhenAndWhy()
    {
        _queues.DiscardAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Discarded);

        await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), " carrier-tracking ", 3, "  Duplicate of SH-0041 ");

        await _audit.Received(1).WriteAsync(
            new AuditEvent("Iris Operator", AuditActions.Discard, "carrier-tracking", "original-1", "corr-1", Discarded.BusinessKeys, Now, "Duplicate of SH-0041"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Handle_WithoutAReason_IsInvalid_DiscardsNothing_AndAuditsNothing(string? reason)
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, reason!);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle(error => error.Identifier == nameof(DiscardDeadLetteredMessageCommand.Reason));
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    [InlineData("carrier-tracking", -1)]
    public async Task Handle_WithoutAQueueOrAValidSequenceNumber_IsInvalid_AndDiscardsNothing(string? entity, long sequenceNumber)
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), entity!, sequenceNumber, "Duplicate");

        result.Status.Should().Be(ResultStatus.Invalid);
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OperatorRoles.Read)]
    [InlineData(null)]
    public async Task Handle_ForAnOperatorWithoutTheResubmitRole_IsForbidden_DiscardsNothing_AndAuditsNothing(string? role)
    {
        var result = await HandleAsync(Operator("Iris Operator", role), "carrier-tracking", 3, "Duplicate");

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAnOperatorWhoIsNotSignedIn_IsForbidden()
    {
        var result = await HandleAsync(new ClaimsPrincipal(new ClaimsIdentity()), "carrier-tracking", 3, "Duplicate");

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAnOperatorWithoutAName_IsForbidden_ForTheAuditCouldNotSayWho()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, OperatorRoles.Resubmit)], "test");

        var result = await HandleAsync(new ClaimsPrincipal(identity), "carrier-tracking", 3, "Duplicate");

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAMessageThatIsGone_IsNotFound_AndAuditsNothing()
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, "Duplicate");

        result.Status.Should().Be(ResultStatus.NotFound);
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForASubscription_DiscardsOnThatSubscription_AndTheAuditEventNamesIt()
    {
        var discarded = new DiscardedDeadLetter("shipment-events/carrier-booking", "original-1", "corr-1", []);
        _queues.DiscardAsync("shipment-events/carrier-booking", 3, Arg.Any<CancellationToken>()).Returns(discarded);

        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "shipment-events/carrier-booking", 3, "Duplicate");

        result.Value.Should().Be(discarded);
        await _audit.Received(1).WriteAsync(
            new AuditEvent("Iris Operator", AuditActions.Discard, "shipment-events/carrier-booking", "original-1", "corr-1", [], Now, "Duplicate"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheCompletionFails_FailsWithoutAnAuditEvent()
    {
        _queues.DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns<DiscardedDeadLetter?>(_ => throw new InvalidOperationException());

        var discard = async () => await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, "Duplicate");

        await discard.Should().ThrowAsync<InvalidOperationException>();
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheOperatorActsOnASuggestion_TheAuditCarriesTheAgentNameAndRunId()
    {
        _queues.DiscardAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Discarded);
        var run = new AgentRun("integration-operations", "run-7");

        await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, "Duplicate", run);

        await _audit.Received(1).WriteAsync(
            new AuditEvent("Iris Operator", AuditActions.Discard, "carrier-tracking", "original-1", "corr-1", Discarded.BusinessKeys, Now, "Duplicate", run),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheOperatorDiscardsOnTheirOwn_TheAuditCarriesNoAgent()
    {
        _queues.DiscardAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Discarded);

        await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, "Duplicate");

        await _audit.Received(1).WriteAsync(Arg.Is<AuditEvent>(auditEvent => auditEvent.Agent == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheOperatorLacksTheResubmitRole_ASuggestionDoesNotChangeThat()
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Read), "carrier-tracking", 3, "Duplicate", new AgentRun("integration-operations", "run-7"));

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForASuggestionWithoutAReason_StillNeedsOne()
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, " ", new AgentRun("integration-operations", "run-7"));

        result.Status.Should().Be(ResultStatus.Invalid);
        await _queues.DidNotReceive().DiscardAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static ClaimsPrincipal Operator(string name, string? role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }

    private async Task<Result<DiscardedDeadLetter>> HandleAsync(ClaimsPrincipal caller, string entity, long sequenceNumber, string reason, AgentRun? actingOn = null) =>
        await new DiscardDeadLetteredMessageHandler(_queues, _audit, _time)
            .Handle(new DiscardDeadLetteredMessageCommand(caller, entity, sequenceNumber, reason, actingOn), TestContext.Current.CancellationToken);
}
