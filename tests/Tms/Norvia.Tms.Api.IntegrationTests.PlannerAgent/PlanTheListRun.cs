using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// One "Plan the list" run (#445) as the TMS UI makes it: the real <see cref="PlanTheListHandler"/>, whose waiting list is the orders a test submitted itself (the shared TMS also holds
/// other tests' orders) and whose every question runs the real Planner agent through the stand-in against the real TMS API. It records each run, so a test can count the tool calls.
/// </summary>
internal sealed class PlanTheListRun
{
    private readonly List<AgentRunResult> _runs = [];

    private PlanTheListRun()
    {
    }

    /// <summary>What the Agent was asked and did, one run per question asked.</summary>
    public IReadOnlyList<AgentRunResult> Runs => _runs;

    public PlannedList Plan { get; private set; } = new([], null);

    public static async Task<PlanTheListRun> ExecuteAsync(
        PlannerAgentFixture fixture, IReadOnlyList<string> waiting, int maxOrders, CancellationToken cancellationToken)
    {
        var run = new PlanTheListRun();
        var agentName = PlannerAgentFixture.NewAgentId();
        var sender = new Sender(waiting, async (query, token) =>
        {
            var result = await fixture.SuggestAsync(agentName, query.TransportOrderNumber, query.AlreadyAssumed ?? [], Guid.NewGuid().ToString(), token);
            run._runs.Add(result);
            return result.Suggestion;
        });
        var planned = await new PlanTheListHandler(sender, new PlanTheListLimits(maxOrders)).Handle(new PlanTheListQuery(), cancellationToken);
        run.Plan = planned.Value;
        return run;
    }

    /// <summary>The tool calls the whole run made.</summary>
    public IEnumerable<string> ToolCalls => _runs.SelectMany(result => result.ToolCalls).Select(call => call.ToolName);

    private sealed class Sender(IReadOnlyList<string> waiting, Func<SuggestAssignmentQuery, CancellationToken, Task<Suggestion>> suggest) : ISender
    {
        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken) =>
            query switch
            {
                ListTransportOrdersToPlanQuery => ValueTask.FromResult((TResponse)(object)Listed()),
                SuggestAssignmentQuery ask => Suggested<TResponse>(ask, cancellationToken),
                _ => throw new NotSupportedException(query.GetType().Name),
            };

        private async ValueTask<TResponse> Suggested<TResponse>(SuggestAssignmentQuery ask, CancellationToken cancellationToken) =>
            (TResponse)(object)Result.Success(await suggest(ask, cancellationToken));

        private IReadOnlyList<TransportOrderToPlanSummary> Listed() =>
            [.. waiting.Select(number => new TransportOrderToPlanSummary(number, RatingResult.Rated, new ShipperView(string.Empty, string.Empty), null, null, null, null, null, DateTimeOffset.UtcNow))];

        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<object?> Send(object message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
