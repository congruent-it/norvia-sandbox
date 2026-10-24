using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;

namespace Norvia.Agents.UnitTests;

/// <summary>The scripted model's answers to the fixed questions of the failed-run and dead-letter Explain buttons and the summary.</summary>
public sealed class ScriptedQuestionsTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.IntegrationOperationsDefinition);
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

    private static FakeToolSource Tools() => new(new Dictionary<string, Func<string>>
    {
        ["get_message_trail"] = () => TrailSamples.AllWell,
        ["get_run"] = () => TrailSamples.Run,
        ["list_failed_runs"] = () => TrailSamples.FailedRuns,
        ["list_dead_letter_queues"] = () => TrailSamples.DeadLetterQueues,
        ["peek_dead_letters"] = () => TrailSamples.PeekedTracking,
    });

    private static Task<AgentRunResult> RunAsync(string question, FakeToolSource? tools = null) =>
        new AgentRunner(IntegrationOperationsScript.Model(), tools ?? Tools(), new AgentRunnerOptions(TimeSpan.FromSeconds(5)))
            .RunAsync(Definition, new AgentQuestion(question, "c-1", "run-1"), CancellationToken.None);

    [Fact]
    public async Task ExplainFailedRun_ARunOfALogicApp_ReadsTheFailedRunsAndTheRun_AndNamesTheFailingStep()
    {
        var result = await RunAsync(AgentQuestions.ExplainFailedRun("carrier-booking", Start));

        result.ToolCalls.Select(call => call.ToolName).Should().Equal("list_failed_runs", "get_run");
        result.Suggestion.Cause.Should().Contain("Call_Carrier").And.Contain("BadRequest").And.Contain("refused");
        result.Suggestion.SuggestedAction.Should().Contain("c-9");
        result.Suggestion.Evidence.Select(evidence => evidence.ResultId).Should().Equal("r1", "r2");
        result.Suggestion.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public async Task ExplainFailedRun_ARunWithNoLogicApp_ReadsOnlyTheFailedRuns()
    {
        var result = await RunAsync(AgentQuestions.ExplainFailedRun("billing", new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero)));

        result.ToolCalls.Select(call => call.ToolName).Should().Equal("list_failed_runs");
        result.Suggestion.Confidence.Should().Be(Confidence.Low);
        result.Suggestion.SuggestedAction.Should().Contain("tracked no Message trail");
    }

    [Fact]
    public async Task ExplainFailedRun_ARunNoLongerListed_SaysSoWithLowConfidence()
    {
        var result = await RunAsync(AgentQuestions.ExplainFailedRun("carrier-booking", Start.AddDays(-3)));

        result.Suggestion.Cause.Should().Contain("isn't among the failed runs");
        result.Suggestion.Confidence.Should().Be(Confidence.Low);
    }

    [Fact]
    public async Task ExplainDeadLetter_TheUnmappableCode_GivesTheDemosAnswer_AfterPeekingTheQueue()
    {
        var tools = Tools();

        var result = await RunAsync(AgentQuestions.ExplainDeadLetter("carrier-tracking", 4), tools);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "peek_dead_letters"));
        result.Suggestion.Cause.Should().Contain("could not map the status code");
        result.Suggestion.SuggestedAction.Should().Contain("POD");
        result.Suggestion.Confidence.Should().Be(Confidence.High);
    }

    [Fact]
    public async Task ExplainDeadLetter_AnotherReason_NamesTheReason()
    {
        var result = await RunAsync(AgentQuestions.ExplainDeadLetter("carrier-tracking", 6));

        result.Suggestion.Cause.Should().Contain("MaxDeliveryCountExceeded");
        result.Suggestion.Evidence.Single().Claim.Should().Contain("after 10 deliveries");
        result.Suggestion.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public async Task ExplainDeadLetter_AMessageGoneFromThePeek_SaysSoWithLowConfidence()
    {
        var result = await RunAsync(AgentQuestions.ExplainDeadLetter("carrier-tracking", 99));

        result.Suggestion.Cause.Should().Contain("isn't among the oldest");
        result.Suggestion.Confidence.Should().Be(Confidence.Low);
    }

    [Fact]
    public async Task WhatNeedsAttention_PeeksEachNonEmptyQueue_AndGroupsThemByReason()
    {
        var peeks = new Queue<string>([TrailSamples.PeekedTracking, TrailSamples.PeekedBilling]);
        var tools = new FakeToolSource(new Dictionary<string, Func<string>>
        {
            ["list_dead_letter_queues"] = () => TrailSamples.DeadLetterQueues,
            ["peek_dead_letters"] = peeks.Dequeue,
        });

        var result = await RunAsync(AgentQuestions.WhatNeedsAttention, tools);

        result.ToolCalls.Select(call => call.ToolName).Should().Equal("list_dead_letter_queues", "peek_dead_letters", "peek_dead_letters");
        result.Suggestion.Cause.Should().Contain("2 queue(s)").And.Contain("2 group(s)");
        result.Suggestion.Evidence.Should().HaveCount(2);
        result.Suggestion.Evidence.Should().Contain(evidence => evidence.Claim.Contains("MaxDeliveryCountExceeded", StringComparison.Ordinal) && evidence.Claim.Contains("carrier-tracking, shipment-events/billing", StringComparison.Ordinal));
        result.Suggestion.Confidence.Should().Be(Confidence.High);
    }

    [Fact]
    public async Task WhatNeedsAttention_WithNoMessagesAnywhere_SaysNothingNeedsAttention_WithoutPeeking()
    {
        var tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["list_dead_letter_queues"] = () => TrailSamples.NoDeadLetters, ["peek_dead_letters"] = () => "{}" });

        var result = await RunAsync(AgentQuestions.WhatNeedsAttention, tools);

        result.ToolCalls.Select(call => call.ToolName).Should().Equal("list_dead_letter_queues");
        result.Suggestion.Cause.Should().Contain("nothing needs attention");
    }

    [Fact]
    public async Task WhatNeedsAttention_WithMoreQueuesThanTheCapAllows_SaysHowManyWereNotRead()
    {
        var queues = string.Join(",", Enumerable.Range(1, 7).Select(number => $$"""{"name":"q{{number}}","count":1,"oldestAgeSeconds":{{100 - number}}}"""));
        var tools = new FakeToolSource(new Dictionary<string, Func<string>>
        {
            ["list_dead_letter_queues"] = () => $$"""{"at":"2026-10-24T10:00:00+00:00","queues":[{{queues}}]}""",
            ["peek_dead_letters"] = () => TrailSamples.PeekedBilling,
        });

        var result = await RunAsync(AgentQuestions.WhatNeedsAttention, tools);

        result.ToolCalls.Should().HaveCount(6);
        result.Suggestion.Cause.Should().Contain("2 more queues hold messages that were not read");
        result.Suggestion.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public void AgentQuestions_ParseWhatTheyBuild_AndNothingElse()
    {
        AgentQuestions.ParseFailedRun(AgentQuestions.ExplainFailedRun("carrier-booking", Start)).Should().Be(("carrier-booking", Start));
        AgentQuestions.ParseDeadLetter(AgentQuestions.ExplainDeadLetter("shipment-events/billing", 12)).Should().Be(("shipment-events/billing", 12L));
        AgentQuestions.IsWhatNeedsAttention(AgentQuestions.WhatNeedsAttention).Should().BeTrue();
        AgentQuestions.ParseFailedRun(AgentQuestions.ExplainTrail("c-1")).Should().BeNull();
        AgentQuestions.ParseDeadLetter(AgentQuestions.ExplainTrail("c-1")).Should().BeNull();
        AgentQuestions.IsWhatNeedsAttention(AgentQuestions.ExplainTrail("c-1")).Should().BeFalse();
    }
}
