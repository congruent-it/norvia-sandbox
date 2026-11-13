using System.Net;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.HealthCheck.Application.Cost;
using static Norvia.HealthCheck.Agents.IntegrationTests.FakeAzure;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The spend page (#506) in Azure as the app builds it: <see cref="GetCostViewQuery"/>, the page's only read, over the app's own cost reader against
/// faked Cost Management and TMS API answers, and the page's route behind its role. The page renders in the browser (a circuit, not prerendered), so
/// what it shows is the UI tests' (#508); these cases are what it is given. The host's clock stands at <see cref="AzureHost.Now"/>: today is
/// 13 November 2026, yesterday the 12th, so the current period is 6 to 12 November and the previous one 30 October to 5 November. Each test
/// starts an app of its own.
/// </summary>
public sealed class CostViewAgainstAzureTests
{
    private const string Sql = $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia";
    private const string Workspace = $"{AzureHost.ResourceGroupId}/providers/Microsoft.OperationalInsights/workspaces/log-norvia";

    private static readonly DateOnly Yesterday = new(2026, 11, 12);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Result<CostView>> ViewAsync(AzureHost host)
    {
        using var scope = host.AppServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetCostViewQuery(), Ct);
    }

    private static CostRow[] EveryDay(DateOnly first, DateOnly last, string resource, Func<DateOnly, decimal> cost) =>
        [.. Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(offset => first.AddDays(offset)).Select(day => new CostRow(day, resource, cost(day)))];

    [Fact]
    public async Task CostView_ReadsTheFourteenWholeDaysEndingYesterday_FromCostManagementAndTheTms_AsTheAppsIdentity()
    {
        await using var host = new AzureHost();

        var result = await ViewAsync(host);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        var cost = host.Azure.Requests.Should().ContainSingle(request => request.Uri.Contains("Microsoft.CostManagement", StringComparison.Ordinal)).Subject;
        cost.Authorization.Should().Be("Bearer app-token");
        cost.Body.Should().Contain("2026-10-30T00:00:00Z").And.Contain("2026-11-12T23:59:59Z");
        var tms = host.Tms.Requests.Should().ContainSingle().Subject;
        tms.Uri.Should().Be($"{AzureHost.TmsAddress}/v1/transport-orders/volume?from=2026-10-30&to=2026-11-12");
        tms.Authorization.Should().Be("Bearer app-token");
    }

    [Fact]
    public async Task CostView_ARiseWithFlatVolume_PutsTheRisingResourceFirst_AndShowsVolumeUnchanged()
    {
        await using var host = new AzureHost();
        var previousFirst = new DateOnly(2026, 10, 30);
        var previousLast = new DateOnly(2026, 11, 5);
        var currentFirst = new DateOnly(2026, 11, 6);
        host.Azure.Cost([
            .. EveryDay(previousFirst, previousLast, Sql, _ => 11m),
            .. EveryDay(currentFirst, Yesterday, Sql, _ => 11m),
            .. EveryDay(previousFirst, previousLast, Workspace, _ => 1m),
            .. EveryDay(currentFirst, Yesterday, Workspace, _ => 6m)]);
        host.Tms.Volume([.. Enumerable.Range(0, 14).Select(offset => (previousFirst.AddDays(offset), 40))]);

        var view = (await ViewAsync(host)).Value;

        view.Resources.Select(resource => resource.Name).Should().Equal("log-norvia", "sql-norvia");
        view.Resources[0].Should().Be(new PeriodChange("log-norvia", 1m, 6m));
        view.Resources[0].Relative.Should().Be(5m);
        view.Resources[1].Relative.Should().Be(0m);
        view.Total.Should().Be(new PeriodChange("Total", 12m, 17m, ChangeKind.Total));
        view.Volume.Should().Be(new PeriodChange("Transport Orders", 40m, 40m, ChangeKind.Volume));
        view.Volume.Relative.Should().Be(0m, "the volume did not move, so the rise has no business reason");
    }

    [Fact]
    public async Task CostView_ShowsYesterdayProvisional_AndLeavesItOutOfTheAverages()
    {
        await using var host = new AzureHost();
        var currentFirst = new DateOnly(2026, 11, 6);
        host.Azure.Cost([
            .. EveryDay(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 5), Sql, _ => 10m),
            .. EveryDay(currentFirst, Yesterday, Sql, day => day == Yesterday ? 1m : 10m)]);

        var view = (await ViewAsync(host)).Value;

        view.Days.Should().HaveCount(14);
        view.Days.Where(day => day.Provisional).Select(day => day.Date).Should().Equal(Yesterday);
        view.HasProvisionalDays.Should().BeTrue();
        view.Resources.Single().Current.Should().Be(10m, "a provisional day is half-settled spend and the page never averages a guess");
        view.Total.Current.Should().Be(10m);
    }

    [Fact]
    public async Task CostView_WhenTheBillHasNoSpend_ShowsEveryWholeDayWithZero_AndNoResource()
    {
        await using var host = new AzureHost();

        var view = (await ViewAsync(host)).Value;

        view.Days.Should().HaveCount(14, "every whole day is a day, even with no spend");
        view.Resources.Should().BeEmpty();
        view.Total.Should().Be(new PeriodChange("Total", 0m, 0m, ChangeKind.Total));
        view.Days.Should().OnlyContain(day => day.Spend.Count == 0);
    }

    [Fact]
    public async Task CostView_ADayWithSpendAndNoOrders_IsComparedAsZeroVolume_NotAsMissing()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(EveryDay(new DateOnly(2026, 11, 6), Yesterday, Sql, _ => 11m));

        var view = (await ViewAsync(host)).Value;

        view.Volume.Should().Be(new PeriodChange("Transport Orders", 0m, 0m, ChangeKind.Volume));
        view.Total.Current.Should().Be(11m);
    }

    [Fact]
    public async Task CostView_WhenTheBillIsNotInEur_Fails()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(new CostRow(new DateOnly(2026, 11, 10), Sql, 11m, "USD"));

        var read = () => ViewAsync(host);

        (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*USD*");
    }

    [Fact]
    public async Task CostView_WhenCostManagementRefuses_Fails()
    {
        await using var host = new AzureHost();
        host.Azure.Fail(HttpStatusCode.Forbidden);

        var read = () => ViewAsync(host);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CostView_WhenTheTmsRefuses_Fails()
    {
        await using var host = new AzureHost();
        host.Tms.Fail(HttpStatusCode.ServiceUnavailable);

        var read = () => ViewAsync(host);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CostView_ReadsOnly_NothingIsPostedButTheCostQuery()
    {
        await using var host = new AzureHost();

        (await ViewAsync(host)).IsSuccess.Should().BeTrue();

        host.Azure.Requests.Should().NotBeEmpty();
        host.Azure.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Post && request.Uri.Contains("Microsoft.CostManagement/query", StringComparison.Ordinal),
            "the page asks for the bill and nothing else of Azure");
        host.Tms.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task SpendPage_ForAnOperatorWithTheCostRole_IsServed()
    {
        await using var host = new AzureHost();

        var response = await host.AppClient().GetAsync("/cost", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SpendPage_ForAnOperatorWithoutTheCostRole_IsRefused()
    {
        await using var host = new AzureHost(operatorRoles: ["HealthCheck.Run", "HealthCheck.Read"]);

        var response = await host.AppClient().GetAsync("/cost", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
