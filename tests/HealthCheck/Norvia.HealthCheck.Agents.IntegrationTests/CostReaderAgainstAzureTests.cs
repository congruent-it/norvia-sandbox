using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Norvia.HealthCheck.Application.Cost;
using static Norvia.HealthCheck.Agents.IntegrationTests.FakeAzure;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The cost view's reader in Azure (#504) as the app builds it: <see cref="ICostReader"/> from the app's own registrations reads Cost Management
/// and the TMS API, both faked over real HTTP pipelines, as the app's managed identity. The host's clock stands at <see cref="AzureHost.Now"/>,
/// so today is 13 November 2026 and yesterday the 12th. Each test starts an app of its own.
/// </summary>
public sealed class CostReaderAgainstAzureTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(AzureHost.Now.UtcDateTime);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<IReadOnlyList<CostDay>> ReadAsync(AzureHost host, DateOnly firstDay, DateOnly lastDay) =>
        host.AppServices.GetRequiredService<ICostReader>().ReadAsync(firstDay, lastDay, Ct);

    [Fact]
    public async Task ReadAsync_PairsEachDaysSpendPerResourceWithThatDaysTransportOrders()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(
            new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/{AzureHost.Namespace}", 4.25m),
            new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m),
            new CostRow(new DateOnly(2026, 11, 11), $"{AzureHost.ResourceGroupId}/providers/Microsoft.ServiceBus/namespaces/{AzureHost.Namespace}", 4.5m));
        host.Tms.Volume((new DateOnly(2026, 11, 10), 38), (new DateOnly(2026, 11, 11), 40));

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11));

        days.Select(day => day.Date).Should().Equal(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11));
        days[0].TransportOrders.Should().Be(38);
        days[0].Spend.Should().BeEquivalentTo([new ResourceSpend(AzureHost.Namespace, 4.25m), new ResourceSpend("sql-norvia", 11m)]);
        days[1].TransportOrders.Should().Be(40);
        days[1].Spend.Should().BeEquivalentTo([new ResourceSpend(AzureHost.Namespace, 4.5m)]);
    }

    [Fact]
    public async Task ReadAsync_AsksOnlyForTheEstatesResourceGroup_AndTheTmsForTheSameDays_AsTheAppsIdentity()
    {
        await using var host = new AzureHost();

        await ReadAsync(host, new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 10));

        var cost = host.Azure.Requests.Should().ContainSingle(request => request.Uri.Contains("Microsoft.CostManagement", StringComparison.Ordinal)).Subject;
        cost.Method.Should().Be(HttpMethod.Post);
        cost.Uri.Should().StartWith($"https://management.azure.com{AzureHost.ResourceGroupId}/providers/Microsoft.CostManagement/query");
        cost.Authorization.Should().Be("Bearer app-token");
        cost.Body.Should().Contain("2026-11-08T00:00:00Z").And.Contain("2026-11-10T23:59:59Z").And.Contain("ActualCost");
        var tms = host.Tms.Requests.Should().ContainSingle().Subject;
        tms.Method.Should().Be(HttpMethod.Get);
        tms.Uri.Should().Be($"{AzureHost.TmsAddress}/v1/transport-orders/volume?from=2026-11-08&to=2026-11-10");
        tms.Authorization.Should().Be("Bearer app-token");
    }

    [Fact]
    public async Task ReadAsync_AsksTheTmsApiForItsOwnScope_NotManagements()
    {
        await using var host = new AzureHost();

        await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        host.TokenScopes.Should().Contain("api://norvia-tms/.default");
    }

    [Fact]
    public async Task ReadAsync_AChargeWithNoResource_IsSpendOfUnassigned()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(new CostRow(new DateOnly(2026, 11, 10), string.Empty, 3m));

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        days.Single().Spend.Should().BeEquivalentTo([new ResourceSpend("unassigned", 3m)]);
    }

    [Fact]
    public async Task ReadAsync_AWholeDayWithoutSpendOrOrders_IsStillADay_WithZeroOrders()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m));
        host.Tms.Volume((new DateOnly(2026, 11, 10), 5));

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11));

        days.Should().HaveCount(2);
        days[1].Should().BeEquivalentTo(new CostDay(new DateOnly(2026, 11, 11), 0, false, []));
    }

    [Fact]
    public async Task ReadAsync_LeavesTodayOut_AndMarksYesterdayProvisional()
    {
        await using var host = new AzureHost();

        var days = await ReadAsync(host, Today.AddDays(-3), Today);

        days.Select(day => day.Date).Should().Equal(Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1));
        days.Select(day => day.Provisional).Should().Equal(false, false, true);
        host.Tms.Requests.Single().Uri.Should().EndWith($"from={Today.AddDays(-3):yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}", "the TMS is asked for the same whole days");
    }

    [Fact]
    public async Task ReadAsync_AWindowOfNothingButToday_AsksNobodyAndAnswersNothing()
    {
        await using var host = new AzureHost();

        var days = await ReadAsync(host, Today, Today);

        days.Should().BeEmpty();
        host.Azure.Requests.Should().BeEmpty();
        host.Tms.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenCostManagementAnswersInPages_ReadsThemAll()
    {
        await using var host = new AzureHost();
        host.Azure.CostPages(
            [new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m)],
            [new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Web/sites/tms-api", 7m)]);

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        days.Single().Spend.Should().BeEquivalentTo([new ResourceSpend("sql-norvia", 11m), new ResourceSpend("tms-api", 7m)]);
        host.Azure.Requests.Count(request => request.Uri.Contains("Microsoft.CostManagement", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task ReadAsync_AChargeOutsideTheWindow_IsNotCounted()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(
            new CostRow(new DateOnly(2026, 11, 9), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 99m),
            new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m));

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        days.Single().Spend.Should().BeEquivalentTo([new ResourceSpend("sql-norvia", 11m)]);
    }

    [Fact]
    public async Task ReadAsync_SeveralChargesOfOneResourceOnADay_AreOneSpend()
    {
        await using var host = new AzureHost();
        host.Azure.CostPages(
            [new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m)],
            [new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 2.5m)]);

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        days.Single().Spend.Should().BeEquivalentTo([new ResourceSpend("sql-norvia", 13.5m)]);
    }

    [Fact]
    public async Task ReadAsync_AChargeInAnotherCurrencyOutsideTheWindow_IsNotAProblem()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(
            new CostRow(new DateOnly(2026, 11, 9), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 99m, "USD"),
            new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m));

        var days = await ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        days.Single().Spend.Should().BeEquivalentTo([new ResourceSpend("sql-norvia", 11m)]);
    }

    [Fact]
    public async Task ReadAsync_WhenTheBillIsNotInEur_Fails_RatherThanAddingCurrencies()
    {
        await using var host = new AzureHost();
        host.Azure.Cost(new CostRow(new DateOnly(2026, 11, 10), $"{AzureHost.ResourceGroupId}/providers/Microsoft.Sql/servers/sql-norvia", 11m, "USD"));

        var read = () => ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*USD*");
    }

    [Fact]
    public async Task ReadAsync_WhenCostManagementRefuses_Fails_AndGuessesNoSpend()
    {
        await using var host = new AzureHost();
        host.Azure.Fail(HttpStatusCode.Forbidden);

        var read = () => ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ReadAsync_WhenTheTmsRefuses_Fails_AndGuessesNoVolume()
    {
        await using var host = new AzureHost();
        host.Tms.Fail(HttpStatusCode.Forbidden);

        var read = () => ReadAsync(host, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        await read.Should().ThrowAsync<HttpRequestException>();
    }
}
