using Norvia.IntegrationConsole.Web.Agents;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

/// <summary>
/// The agent view is an allowlist of fields (ADR 0040): a field a use case gains later stays out of an Agent's reach until it is
/// added here, in the view and in this list.
/// </summary>
public sealed class AgentViewsTests
{
    [Theory]
    [InlineData(typeof(AgentTrailView), new[] { "CorrelationId", "Hops", "BusinessKeys" })]
    [InlineData(typeof(AgentHopView), new[] { "Name", "Flow", "Time", "DurationMs", "Outcome", "FailureCode", "Fault", "AgentId", "ToolName", "Run", "DeadLetter" })]
    [InlineData(typeof(AgentRunReference), new[] { "LogicApp", "Workflow", "RunId" })]
    [InlineData(typeof(AgentBusinessKeyView), new[] { "Kind", "Value" })]
    [InlineData(typeof(AgentRunView), new[] { "LogicApp", "Workflow", "RunId", "IsKept", "Status", "Steps" })]
    [InlineData(typeof(AgentStepView), new[] { "Name", "Status", "DurationMs", "Error" })]
    [InlineData(typeof(AgentStepError), new[] { "Code", "Message" })]
    [InlineData(typeof(AgentFailedRunsView), new[] { "From", "To", "Runs" })]
    [InlineData(typeof(AgentFailedRunView), new[] { "Flow", "Run", "Start", "CorrelationId", "FailedStep", "Error" })]
    [InlineData(typeof(AgentDeadLetterQueuesView), new[] { "At", "Queues" })]
    [InlineData(typeof(AgentDeadLetterQueueView), new[] { "Name", "Count", "OldestAgeSeconds" })]
    [InlineData(typeof(AgentDeadLettersView), new[] { "Entity", "Messages", "HasMore" })]
    [InlineData(typeof(AgentDeadLetterView), new[] { "SequenceNumber", "EnqueuedAt", "Reason", "Description", "DeliveryCount", "CorrelationId", "BusinessKeys", "ApplicationProperties" })]
    public void TheView_HasExactlyTheAllowedFields(Type view, string[] allowed)
    {
        view.GetProperties().Select(property => property.Name).Should().BeEquivalentTo(allowed);
    }

    [Fact]
    public void TheView_HasNoFieldThatCouldHoldABody()
    {
        var names = new[] { typeof(AgentTrailView), typeof(AgentHopView), typeof(AgentRunView), typeof(AgentStepView), typeof(AgentStepError), typeof(AgentFailedRunView), typeof(AgentDeadLetterQueueView), typeof(AgentDeadLetterView) }
            .SelectMany(view => view.GetProperties()).Select(property => property.Name);

        names.Should().NotContain(name => name.Contains("Body", StringComparison.OrdinalIgnoreCase) || name.Contains("Input", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Output", StringComparison.OrdinalIgnoreCase) || name.Contains("Content", StringComparison.OrdinalIgnoreCase) || name.Contains("Actor", StringComparison.OrdinalIgnoreCase));
    }
}
