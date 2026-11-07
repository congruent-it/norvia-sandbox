using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Norvia.Tms.Web.UiTests.CustomerServiceAgent.StopsTheAgentFirst))]

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Runs the journey that reads a Transport Order through several tool calls last. Once it has, Aspire fails to stop the MCP stand-in (its graceful shutdown outlasts the
/// stop command), which the journeys of an Agent that fails need. The tests still set up their own data and none relies on another's result.
/// </summary>
public sealed class StopsTheAgentFirst : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections.OrderBy(collection => collection.TestCollectionDisplayName.Contains(nameof(ExplainTransportOrderJourneyTests), StringComparison.Ordinal) ? 1 : 0)];
}
