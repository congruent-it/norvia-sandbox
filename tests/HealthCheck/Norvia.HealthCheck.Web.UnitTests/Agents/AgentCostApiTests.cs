// NSubstitute sets up a ValueTask-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Run;
using TestContext = Xunit.TestContext;

namespace Norvia.HealthCheck.Web.UnitTests.Agents;

/// <summary>The cost tools of the agent API (#511): the FinOps agent's two reads, behind a call role of their own.</summary>
public sealed class AgentCostApiTests : IAsyncLifetime
{
    private const string CostRole = "HealthCheck.Cost.Read";

    private static readonly CostView View = new(
        7,
        [],
        [new PeriodChange("log-norvia", 0.9m, 4.2m), new PeriodChange("sb-norvia", 3.2m, 3.2m), new PeriodChange("new-thing", 0m, 0.5m)],
        new PeriodChange("Total", 4.1m, 7.4m, ChangeKind.Total),
        new PeriodChange("Transport Orders", 40m, 41m, ChangeKind.Volume));

    private static readonly HealthCheckReport Report = new(
        "as-found",
        new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero),
        3,
        [
            new Finding("F-DLQ-ALERT", "DLQ-ALERT", "A dead-letter queue has no alert", "Every queue has an alert.", Severity.High, [new Evidence("billing", "No alert.")], "entitiesWithoutDeadLetterAlert", "Remove it."),
            new Finding("F-COST-RISING-SPEND", RisingSpendCheck.CheckId, "A resource's spend rises with no more Transport Orders", "No rise.", Severity.Medium, [new Evidence("log-norvia", "Daily spend rose from 0.90 to 4.20 EUR (+367%).")], "chattyTelemetry", "Set chattyTelemetry to false."),
        ]);

    private AgentApiHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await AgentApiHost.StartAsync();
        _host.Mediator.Send(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(View)));
        _host.Mediator.Send(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Report)));
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> GetAsync(string path, string? token) =>
        await _host.Client.SendAsync(AgentApiHost.Get(path, token), TestContext.Current.CancellationToken);

    [Fact]
    public async Task GetCost_AnswersEveryChangeWorkedOutByCode_AndNothingElse()
    {
        using var response = await GetAsync("/agent/v1/cost", _host.Token(roles: [CostRole]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        view.Select(property => property.Key).Should().BeEquivalentTo("periodDays", "excludesProvisionalDays", "total", "transportOrders", "resources");
        view["periodDays"]!.GetValue<int>().Should().Be(7);
        view["total"]!["change"]!.GetValue<string>().Should().Be("+80%");
        view["transportOrders"]!["change"]!.GetValue<string>().Should().Be("+3%");
        var resources = view["resources"]!.AsArray();
        resources.Select(resource => resource!["name"]!.GetValue<string>()).Should().Equal("log-norvia", "sb-norvia", "new-thing");
        resources[0]!["change"]!.GetValue<string>().Should().Be("+367%");
        resources[1]!["change"]!.GetValue<string>().Should().Be("0%");
        resources[2]!["change"]!.GetValue<string>().Should().Be("n/a", because: "a resource with no spend before has no ratio, and the view never invents one");
        resources[0]!["previousEurPerDay"]!.GetValue<decimal>().Should().Be(0.9m);
        resources[0]!["currentEurPerDay"]!.GetValue<decimal>().Should().Be(4.2m);
    }

    [Fact]
    public async Task GetCost_WhenTheSpendCouldNotBeRead_IsRefused503()
    {
        _host.Mediator.Send(Arg.Any<GetCostViewQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<CostView>.Unavailable("The spend could not be read.")));

        using var response = await GetAsync("/agent/v1/cost", _host.Token(roles: [CostRole]));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetRisingSpend_AnswersTheRisingSpendCheckFindingsAlone_WithTheChecksOwnToggleAndFix()
    {
        using var response = await GetAsync("/agent/v1/rising-spend", _host.Token(roles: [CostRole]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        var finding = view["findings"]!.AsArray().Should().ContainSingle().Subject!;
        finding["id"]!.GetValue<string>().Should().Be("F-COST-RISING-SPEND");
        finding["asFoundToggle"]!.GetValue<string>().Should().Be("chattyTelemetry");
        finding["fix"]!.GetValue<string>().Should().Be("Set chattyTelemetry to false.");
        finding["evidence"]!.AsArray().Single()!["subject"]!.GetValue<string>().Should().Be("log-norvia");
        await _host.Mediator.Received(1).Send(new RunHealthCheckQuery(ReuseWithin: TimeSpan.FromSeconds(60)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/agent/v1/cost")]
    [InlineData("/agent/v1/rising-spend")]
    public async Task TheCostTools_RefuseTheHealthCheckAgentsToken_AndTheHealthCheckToolsRefuseTheCostAgents(string cost)
    {
        using var healthCheckAgentOnCost = await GetAsync(cost, _host.Token());
        using var costAgentOnChecks = await GetAsync("/agent/v1/checks", _host.Token(roles: [CostRole]));
        using var noToken = await GetAsync(cost, null);

        healthCheckAgentOnCost.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        costAgentOnChecks.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
