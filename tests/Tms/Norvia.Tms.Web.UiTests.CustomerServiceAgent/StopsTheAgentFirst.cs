using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Norvia.Tms.Web.UiTests.CustomerServiceAgent.StopsTheAgentFirst))]

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Runs the classes whose journeys stop the MCP stand-in before any other that calls it, and the journey that reads a Transport Order through several tool calls last. Once the stand-in has
/// served calls, Aspire fails to stop it (its graceful shutdown outlasts the stop command), which the journeys of an Agent that fails need; it then stays down for every test after.
/// xUnit does not fix the order of the classes, so it is set here. The tests still set up their own data and none relies on another's result.
/// </summary>
public sealed class StopsTheAgentFirst : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections.OrderBy(collection => Rank(collection)).ThenBy(collection => collection.TestCollectionDisplayName, StringComparer.Ordinal)];

    private static int Rank(ITestCollection collection)
    {
        var name = collection.TestCollectionDisplayName;
        if (name.Contains(nameof(ExplainTransportOrderFailureJourneyTests), StringComparison.Ordinal))
        {
            return 0;
        }

        if (name.Contains(nameof(ExplainRejectedBookingJourneyTests), StringComparison.Ordinal))
        {
            return 1;
        }

        return name.Contains(nameof(ExplainTransportOrderJourneyTests), StringComparison.Ordinal) ? 3 : 2;
    }
}
