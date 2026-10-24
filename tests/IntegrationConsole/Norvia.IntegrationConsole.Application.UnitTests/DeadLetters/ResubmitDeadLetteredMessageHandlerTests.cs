using System.Security.Claims;
using Ardalis.Result;
using Microsoft.Extensions.Time.Testing;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;
using Norvia.IntegrationConsole.Application.Operators;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.UnitTests.DeadLetters;

public sealed class ResubmitDeadLetteredMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly ResubmittedDeadLetter Resubmitted =
        new("carrier-tracking", "original-1", "copy-1", "corr-1", [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0042")]);

    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_ForAnOperatorWithTheResubmitRole_ResubmitsTheMessage_AndReportsTheOutcome()
    {
        _queues.ResubmitAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Resubmitted);

        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3);

        result.Value.Should().Be(Resubmitted);
    }

    [Fact]
    public async Task Handle_WhenTheMessageIsResubmitted_WritesOneAuditEvent_WithWhoWhatWhichMessageAndWhen()
    {
        _queues.ResubmitAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Resubmitted);

        await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), " carrier-tracking ", 3);

        await _audit.Received(1).WriteAsync(
            new AuditEvent("Iris Operator", AuditActions.Resubmit, "carrier-tracking", "original-1", "corr-1", Resubmitted.BusinessKeys, Now),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OperatorRoles.Read)]
    [InlineData(null)]
    public async Task Handle_ForAnOperatorWithoutTheResubmitRole_IsForbidden_ResubmitsNothing_AndAuditsNothing(string? role)
    {
        var result = await HandleAsync(Operator("Iris Operator", role), "carrier-tracking", 3);

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAnOperatorWhoIsNotSignedIn_IsForbidden()
    {
        var result = await HandleAsync(new ClaimsPrincipal(new ClaimsIdentity()), "carrier-tracking", 3);

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAnOperatorWithoutAName_IsForbidden_ForTheAuditCouldNotSayWho()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, OperatorRoles.Resubmit)], "test");

        var result = await HandleAsync(new ClaimsPrincipal(identity), "carrier-tracking", 3);

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAMessageThatIsGone_IsNotFound_AndAuditsNothing()
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("carrier-tracking", -1)]
    public async Task Handle_WithoutAQueueOrAValidSequenceNumber_IsInvalid_AndResubmitsNothing(string entity, long sequenceNumber)
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), entity, sequenceNumber);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _queues.DidNotReceive().ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForASubscription_ResubmitsToThatSubscription_AndTheAuditEventNamesIt()
    {
        var resubmitted = new ResubmittedDeadLetter("shipment-events/carrier-booking", "original-1", "copy-1", "corr-1", []);
        _queues.ResubmitAsync("shipment-events/carrier-booking", 3, Arg.Any<CancellationToken>()).Returns(resubmitted);

        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "shipment-events/carrier-booking", 3);

        result.Value.Should().Be(resubmitted);
        await _audit.Received(1).WriteAsync(
            new AuditEvent("Iris Operator", AuditActions.Resubmit, "shipment-events/carrier-booking", "original-1", "corr-1", [], Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheSendFails_FailsWithoutAnAuditEvent()
    {
        _queues.ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns<ResubmittedDeadLetter?>(_ => throw new InvalidOperationException());

        var resubmit = async () => await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3);

        await resubmit.Should().ThrowAsync<InvalidOperationException>();
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_Always_HasNoBodyToPass()
    {
        typeof(ResubmitDeadLetteredMessageCommand).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(nameof(ResubmitDeadLetteredMessageCommand.Caller), nameof(ResubmitDeadLetteredMessageCommand.Entity), nameof(ResubmitDeadLetteredMessageCommand.SequenceNumber), nameof(ResubmitDeadLetteredMessageCommand.ActingOn));
    }

    [Fact]
    public async Task Handle_WhenTheOperatorActsOnASuggestion_TheAuditCarriesTheAgentNameAndRunId()
    {
        _queues.ResubmitAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Resubmitted);
        var run = new AgentRun("integration-operations", "run-7");

        await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3, run);

        await _audit.Received(1).WriteAsync(
            new AuditEvent("Iris Operator", AuditActions.Resubmit, "carrier-tracking", "original-1", "corr-1", Resubmitted.BusinessKeys, Now, Agent: run),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheOperatorResubmitsOnTheirOwn_TheAuditCarriesNoAgent()
    {
        _queues.ResubmitAsync("carrier-tracking", 3, Arg.Any<CancellationToken>()).Returns(Resubmitted);

        await HandleAsync(Operator("Iris Operator", OperatorRoles.Resubmit), "carrier-tracking", 3);

        await _audit.Received(1).WriteAsync(Arg.Is<AuditEvent>(auditEvent => auditEvent.Agent == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheOperatorLacksTheResubmitRole_ASuggestionDoesNotChangeThat()
    {
        var result = await HandleAsync(Operator("Iris Operator", OperatorRoles.Read), "carrier-tracking", 3, new AgentRun("integration-operations", "run-7"));

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _queues.DidNotReceive().ResubmitAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
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

    private async Task<Result<ResubmittedDeadLetter>> HandleAsync(ClaimsPrincipal caller, string entity, long sequenceNumber, AgentRun? actingOn = null) =>
        await new ResubmitDeadLetteredMessageHandler(_queues, _audit, _time)
            .Handle(new ResubmitDeadLetteredMessageCommand(caller, entity, sequenceNumber, actingOn), TestContext.Current.CancellationToken);
}
