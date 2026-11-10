using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Norvia.Tms.Web.UiTests.PlannerAgent.FailureJourneyFirstOrderer))]

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// Runs the class whose journey stops the MCP stand-in before any other that calls it. Once the stand-in has served calls, Aspire fails to stop it (its graceful shutdown outlasts the stop
/// command), which the journey of an Agent that fails needs. xUnit does not fix the order of the classes, so it is set here. The tests still set up their own data and none relies on another's result.
/// </summary>
public sealed class FailureJourneyFirstOrderer : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections.OrderBy(collection => collection.TestCollectionDisplayName.Contains(nameof(SuggestAssignmentFailureJourneyTests), StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(collection => collection.TestCollectionDisplayName, StringComparer.Ordinal)];
}
