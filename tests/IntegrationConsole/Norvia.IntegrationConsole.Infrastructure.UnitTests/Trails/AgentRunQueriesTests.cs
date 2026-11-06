using Norvia.IntegrationConsole.Infrastructure.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.UnitTests.Trails;

/// <summary>
/// An Agent's call through an MCP server has a correlation ID of its own, made by API Management, and records the run it belongs to as
/// its <c>runId</c>: the run's calls are on the run's trail. What the KQL answers is checked against the workspace by the smoke run
/// (infra/smoke-test-estate-health-check.ps1); here only that the queries still say so.
/// </summary>
public sealed class AgentRunQueriesTests
{
    [Fact]
    public void TrailQuery_TakesTheRunIdOfAnAgentCall_AsItsTrail()
    {
        var kql = TrailQuery.For("run-1", 20);

        kql.Should().Contain("correlationId = coalesce(tostring(Properties.runId), tostring(Properties.correlationId)");
    }

    [Fact]
    public void HopQuery_TakesTheRunIdOfAnAgentCall_AsItsTrail_AndThePathsRequestsOfTheSameOperationWithIt()
    {
        var kql = HopQuery.For("run-1");

        kql.Should().Contain("tostring(Properties.runId) =~ searched");
        kql.Should().Contain("OperationId in (agentRunOperations) and Name contains \"/agent\"");
        kql.Should().Contain("coalesce(tostring(Properties.runId), tostring(Properties.correlationId)");
    }

    [Fact]
    public void HopQuery_StillFindsATrailByItsOwnCorrelationId()
    {
        HopQuery.For("run-1").Should().Contain("tostring(Properties[\"Response-X-Correlation-ID\"]), tostring(Properties[\"Request-X-Correlation-ID\"])")
            .And.Contain("where correlationId =~ searched");
    }
}
