using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Norvia.Erp.Web.UiTests.FinanceAgent.StopsTheAgentFirst))]

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// Runs the journeys that read a line through the MCP stand-in last, so the one that stops it goes first. It finds them by the full name of their class, which the compiler checks. Once they have, Aspire may fail to stop the stand-in (its graceful shutdown outlasts the stop command),
/// which the journey of an Agent that fails needs. The tests still set up their own data and none relies on another's result.
/// </summary>
public sealed class StopsTheAgentFirst : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections.OrderBy(collection => collection.TestCollectionDisplayName.Contains(typeof(ExplainJourneyTests).FullName!, StringComparison.Ordinal) ? 1 : 0)];
}
