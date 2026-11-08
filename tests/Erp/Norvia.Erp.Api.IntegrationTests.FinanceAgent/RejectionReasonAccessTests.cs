using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.AppHost;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// The boundary of the Carrier invoice rejection reason agent is its tool (#473): its identity can call <c>get_carrier_visible_line_facts</c> and nothing else. The stand-in refuses (403) every
/// other tool of every Agent's server on its own server, the ERP API refuses (403) its role on the Finance agent view and the Finance agent's role on its view, and neither Agent's token is for the
/// other's server (401). Each test calls with an Agent identity of its own, as the rate limit counts the calls of one.
/// </summary>
public sealed class RejectionReasonAccessTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    /// <summary>Every tool of every other Agent's server, from the contract files the Bicep reads too.</summary>
    public static IReadOnlyList<string> OtherToolNames() =>
    [
        .. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "contracts"), "*.tools.json")
            .Where(file => !Path.GetFileName(file).StartsWith("finance-rejection-reason.", StringComparison.Ordinal) && !Path.GetFileName(file).StartsWith("example.", StringComparison.Ordinal))
            .SelectMany(file => JsonNode.Parse(File.ReadAllText(file))!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()))
            .Distinct(StringComparer.Ordinal),
    ];

    public static TheoryData<string> OtherTools() => [.. OtherToolNames()];

    [Fact]
    public async Task TheServer_ListsExactlyOneTool_GetCarrierVisibleLineFacts()
    {
        using var client = fixture.CreateMcpClient(fixture.RejectionReasonToken());

        using var response = await client.PostAsJsonAsync(
            new Uri(FinanceAgentFixture.RejectionReasonMcpPath, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list", ["params"] = new JsonObject() },
            _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tools = (await response.Content.ReadFromJsonAsync<JsonObject>(_cancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal(RejectionReasonCalls.Tool);
    }

    [Fact]
    public void TheOtherTools_IncludeTheFinanceMatchingAgentsTwo_SoTheTheoryBelowIsNotVacuous()
    {
        OtherToolNames().Should().Contain([FinanceAgentCalls.Tool, FinanceAgentCalls.SummaryTool]);
    }

    [Theory]
    [MemberData(nameof(OtherTools))]
    public async Task ACallOfAnyOtherAgentViewTool_ByTheRejectionReasonAgent_IsRefused403(string tool)
    {
        using var client = fixture.CreateMcpClient(fixture.RejectionReasonToken());

        var (status, _, _) = await client.CallToolAtAsync(
            FinanceAgentFixture.RejectionReasonMcpPath,
            tool,
            new JsonObject { ["carrierId"] = FinanceAgentCalls.Carrier, ["invoiceNumber"] = FinanceAgentCalls.NewInvoiceNumber(), ["carrierBookingReference"] = FinanceAgentCalls.NewReference() },
            Guid.NewGuid().ToString(),
            _cancellationToken);

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallOfTheTool_WithATokenWithoutTheRejectionReasonRole_IsRefused403()
    {
        using var client = fixture.CreateMcpClient(fixture.RejectionReasonToken(roles: [LocalTokens.FinanceAgentReadRole]));

        var (status, _, _) = await CallFactsAsync(client);

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallOfTheTool_WithNoToken_IsRefused401()
    {
        using var client = fixture.CreateMcpClient(token: null);

        var (status, _, _) = await CallFactsAsync(client);

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheFinanceMatchingAgentsToken_IsNotForTheRejectionReasonServer_AndTheOtherWayRound()
    {
        using var matching = fixture.CreateMcpClient(fixture.AgentToken());
        using var reason = fixture.CreateMcpClient(fixture.RejectionReasonToken());

        var (matchingOnReason, _, _) = await CallFactsAsync(matching);
        var (reasonOnMatching, _, _) = await reason.CallToolAtAsync(
            FinanceAgentFixture.McpPath,
            FinanceAgentCalls.Tool,
            new JsonObject { ["carrierId"] = FinanceAgentCalls.Carrier, ["invoiceNumber"] = FinanceAgentCalls.NewInvoiceNumber(), ["carrierBookingReference"] = FinanceAgentCalls.NewReference() },
            Guid.NewGuid().ToString(),
            _cancellationToken);

        matchingOnReason.Should().Be(HttpStatusCode.Unauthorized, "the Finance agent's token is for another audience");
        reasonOnMatching.Should().Be(HttpStatusCode.Unauthorized, "the rejection reason agent's token is for another audience");
    }

    [Fact]
    public async Task TheRejectionReasonAgentsRole_IsRefused403OnTheFinanceAgentView_AndTheFinanceAgentsRoleOnTheRejectionReasonView()
    {
        using var onFinance = fixture.CreateErpClient(fixture.AgentViewToken([LocalTokens.FinanceRejectionReasonAgentReadRole]));
        using var onReason = fixture.CreateErpClient(fixture.AgentViewToken([LocalTokens.FinanceAgentReadRole]));
        var line = $"carriers/{FinanceAgentCalls.Carrier}/invoices/{FinanceAgentCalls.NewInvoiceNumber()}/lines/{FinanceAgentCalls.NewReference()}";

        using var financeView = await onFinance.GetAsync(new Uri($"/v1/agent/finance/{line}", UriKind.Relative), _cancellationToken);
        using var summaryView = await onFinance.GetAsync(new Uri($"/v1/agent/finance/carriers/{FinanceAgentCalls.Carrier}/invoices/{FinanceAgentCalls.NewInvoiceNumber()}", UriKind.Relative), _cancellationToken);
        using var reasonView = await onReason.GetAsync(new Uri($"/v1/agent/finance-rejection-reason/{line}/carrier-visible-facts", UriKind.Relative), _cancellationToken);

        financeView.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        summaryView.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reasonView.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(FinanceAgentFixture.RecordCarrierCostRole)]
    [InlineData(FinanceAgentFixture.ReceiveCarrierInvoiceRole)]
    [InlineData(FinanceAgentFixture.ManageCarrierInvoiceRole)]
    public async Task ARead_OfTheRejectionReasonView_WithAnotherRole_IsRefused403(string role)
    {
        using var client = fixture.CreateErpClient(fixture.AgentViewToken([role]));

        using var response = await client.GetAsync(
            new Uri($"/v1/agent/finance-rejection-reason/carriers/{FinanceAgentCalls.Carrier}/invoices/{FinanceAgentCalls.NewInvoiceNumber()}/lines/{FinanceAgentCalls.NewReference()}/carrier-visible-facts", UriKind.Relative),
            _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<(HttpStatusCode Status, bool IsError, string Text)> CallFactsAsync(HttpClient client) =>
        client.CallToolAtAsync(
            FinanceAgentFixture.RejectionReasonMcpPath,
            RejectionReasonCalls.Tool,
            new JsonObject { ["carrierId"] = FinanceAgentCalls.Carrier, ["invoiceNumber"] = FinanceAgentCalls.NewInvoiceNumber(), ["carrierBookingReference"] = FinanceAgentCalls.NewReference() },
            Guid.NewGuid().ToString(),
            _cancellationToken);
}
