using System.Security.Claims;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Agents;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// Acting on a Suggestion over the Service Bus emulator: the console's resubmit use case with its real Service Bus adapter and audit log, and the
/// Agent run the console's own Agent client gives a Suggestion. Each test has a queue of its own and a correlation ID that is new, which is also how
/// it finds its audit event, so none depends on another or on the order they run in.
/// </summary>
public sealed class ResubmitFromSuggestionTests(AgentServiceBusFixture serviceBus)
{
    private const string Reason = "UnmappableTrackingMessage";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ActingOnASuggestion_ResubmitsToItsQueue_AndAuditsTheAgentNameAndRunId()
    {
        await using var host = NewHost();
        var (correlationId, original, sequence) = await DeadLetterAsync(AgentServiceBusFixture.ActedOnQueue, null);
        var run = await RunOfSuggestionAsync(host, AgentServiceBusFixture.ActedOnQueue, sequence, correlationId);

        var result = await ResubmitAsync(host, Operator(OperatorRoles.Read, OperatorRoles.Resubmit), AgentServiceBusFixture.ActedOnQueue, sequence, run);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        var copy = Assert.Single(await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.ActedOnQueue, null, 1));
        copy.CorrelationId.Should().Be(correlationId);
        copy.ApplicationProperties["resubmittedFrom"].Should().Be(original.MessageId);
        var audit = Assert.Single(host.Telemetry.Audits, audit => audit.Keys.GetValueOrDefault("correlationId") == correlationId);
        audit.Keys["agent.name"].Should().Be(run.AgentName);
        audit.Keys["agent.run.id"].Should().Be(run.RunId);
        audit.Message.Should().Contain("Iris Operator did resubmit").And.Contain(run.RunId);
    }

    [Fact]
    public async Task ActingOnASuggestionOfASubscriptionsMessage_ResubmitsToThatSubscriptionOnly()
    {
        await using var host = NewHost();
        var (correlationId, _, sequence) = await DeadLetterAsync(AgentServiceBusFixture.ResubmitTopic, AgentServiceBusFixture.SubscriptionA);
        // The topic gave the other subscription the original too; it takes that off, so only a resubmitted copy could still be there.
        await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.ResubmitTopic, AgentServiceBusFixture.SubscriptionB, 1);
        var entity = $"{AgentServiceBusFixture.ResubmitTopic}/{AgentServiceBusFixture.SubscriptionA}";
        var run = await RunOfSuggestionAsync(host, entity, sequence, correlationId);

        var result = await ResubmitAsync(host, Operator(OperatorRoles.Read, OperatorRoles.Resubmit), entity, sequence, run);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        var copy = Assert.Single(await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.ResubmitTopic, AgentServiceBusFixture.SubscriptionA, 1));
        copy.ApplicationProperties["resubmitTo"].Should().Be(AgentServiceBusFixture.SubscriptionA);
        (await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.ResubmitTopic, AgentServiceBusFixture.SubscriptionB, 1, TimeSpan.FromSeconds(3)))
            .Should().BeEmpty("the topic's other subscription must not run again");
        Assert.Single(host.Telemetry.Audits, audit => audit.Keys.GetValueOrDefault("correlationId") == correlationId)
            .Keys["agent.run.id"].Should().Be(run.RunId);
    }

    [Fact]
    public async Task TheSameResubmitWithoutASuggestion_Resubmits_AndAuditsNoAgent()
    {
        await using var host = NewHost();
        var (correlationId, _, sequence) = await DeadLetterAsync(AgentServiceBusFixture.OwnClickQueue, null);

        var result = await ResubmitAsync(host, Operator(OperatorRoles.Read, OperatorRoles.Resubmit), AgentServiceBusFixture.OwnClickQueue, sequence, null);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        Assert.Single(await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.OwnClickQueue, null, 1));
        var audit = Assert.Single(host.Telemetry.Audits, audit => audit.Keys.GetValueOrDefault("correlationId") == correlationId);
        audit.Keys.Should().NotContainKey("agent.name").And.NotContainKey("agent.run.id");
        audit.Message.Should().Contain("Iris Operator did resubmit");
    }

    [Fact]
    public async Task ASuggestionTheOperatorIgnores_ChangesNothingAndWritesNoAudit()
    {
        await using var host = NewHost();
        var (correlationId, _, sequence) = await DeadLetterAsync(AgentServiceBusFixture.IgnoredQueue, null);
        var before = await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.IgnoredQueue, null);

        var run = await RunOfSuggestionAsync(host, AgentServiceBusFixture.IgnoredQueue, sequence, correlationId);

        run.RunId.Should().NotBeNullOrEmpty("the Suggestion is there to be acted on");
        host.Telemetry.Audits.Should().NotContain(audit => audit.Keys.GetValueOrDefault("correlationId") == correlationId);
        var after = await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.IgnoredQueue, null);
        after.Select(message => (message.SequenceNumber, message.DeliveryCount)).Should().Equal(before.Select(message => (message.SequenceNumber, message.DeliveryCount)));
        (await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.IgnoredQueue, null, 1, TimeSpan.FromSeconds(3))).Should().BeEmpty("nothing was resubmitted");
    }

    [Fact]
    public async Task AUserWithoutTheResubmitRole_CannotActOnASuggestion_SoNothingIsSentOrAudited()
    {
        await using var host = NewHost();
        var (correlationId, _, sequence) = await DeadLetterAsync(AgentServiceBusFixture.ForbiddenQueue, null);
        var run = await RunOfSuggestionAsync(host, AgentServiceBusFixture.ForbiddenQueue, sequence, correlationId);

        var result = await ResubmitAsync(host, Operator(OperatorRoles.Read), AgentServiceBusFixture.ForbiddenQueue, sequence, run);

        result.Status.Should().Be(ResultStatus.Forbidden);
        (await Emulator.TakeAsync(serviceBus.Client, AgentServiceBusFixture.ForbiddenQueue, null, 1, TimeSpan.FromSeconds(3))).Should().BeEmpty("nothing was resubmitted");
        (await Emulator.PeekDeadLettersAsync(serviceBus.Client, AgentServiceBusFixture.ForbiddenQueue, null)).Should().ContainSingle(message => message.SequenceNumber == sequence);
        host.Telemetry.Audits.Should().NotContain(audit => audit.Keys.GetValueOrDefault("correlationId") == correlationId);
    }

    private AgentHost NewHost() => new(consoleSettings: serviceBus.ConsoleSettings);

    private static ClaimsPrincipal Operator(params string[] roles) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Iris Operator"), .. roles.Select(role => new Claim(ClaimTypes.Role, role))], "Test"));

    /// <summary>Dead-letters one message of a queue or a subscription's topic and returns its correlation ID, the original and its sequence number.</summary>
    private async Task<(string CorrelationId, Azure.Messaging.ServiceBus.ServiceBusMessage Original, long Sequence)> DeadLetterAsync(string queueOrTopic, string? subscription)
    {
        var correlationId = Guid.NewGuid().ToString();
        var original = Emulator.Message(correlationId, "{\"status\":\"DLV\"}");
        await Emulator.DeadLetterAsync(serviceBus.Client, queueOrTopic, subscription, Reason, original);
        var sequence = (await Emulator.PeekDeadLettersAsync(serviceBus.Client, queueOrTopic, subscription)).Single(message => message.CorrelationId == correlationId).SequenceNumber;
        return (correlationId, original, sequence);
    }

    /// <summary>Asks the Agent to explain the message, as the console does, and returns the run its Suggestion carries.</summary>
    private static async Task<AgentRun> RunOfSuggestionAsync(AgentHost host, string entity, long sequence, string correlationId)
    {
        using var scope = host.ConsoleServices.CreateScope();
        var explained = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ExplainDeadLetterQuery(entity, sequence, correlationId), Ct);
        explained.IsSuccess.Should().BeTrue(string.Join("; ", explained.Errors));
        return explained.Value.Run ?? throw new InvalidOperationException("The Agent client gave the Suggestion no run.");
    }

    private static async Task<Result<ResubmittedDeadLetter>> ResubmitAsync(AgentHost host, ClaimsPrincipal caller, string entity, long sequence, AgentRun? actingOn)
    {
        using var scope = host.ConsoleServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ResubmitDeadLetteredMessageCommand(caller, entity, sequence, actingOn), Ct);
    }
}
