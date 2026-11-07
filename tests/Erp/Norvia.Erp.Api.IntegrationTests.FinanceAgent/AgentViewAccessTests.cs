using System.Net;
using Norvia.AppHost;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// Who may read the ERP API's Finance agent view: only a token for the ERP API that holds the one read role of the Carrier invoice matching agent. The stand-in signs such a token for
/// the Agent's identity; a Logic App's identity, which holds roles of its own, is not let in.
/// </summary>
public sealed class AgentViewAccessTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReadWithNoToken_IsRefused401()
    {
        using var client = fixture.CreateErpClient(token: null);

        using var response = await client.GetAsync(ViewPath(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AReadWithATokenTheErpDoesNotTrust_IsRefused401()
    {
        using var client = fixture.CreateErpClient(
            fixture.AgentViewToken([LocalTokens.FinanceAgentReadRole], signingKey: Convert.ToHexString(Guid.NewGuid().ToByteArray())));

        using var response = await client.GetAsync(ViewPath(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(FinanceAgentFixture.RecordCarrierCostRole)]
    [InlineData(FinanceAgentFixture.ReceiveCarrierInvoiceRole)]
    [InlineData("Something.Else")]
    public async Task AReadWithATokenWithoutTheAgentsReadRole_IsRefused403(string role)
    {
        using var client = fixture.CreateErpClient(fixture.AgentViewToken([role]));

        using var response = await client.GetAsync(ViewPath(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AReadOfALineThatIsNotThere_WithTheAgentsReadRole_Is404()
    {
        using var client = fixture.CreateErpClient(fixture.AgentViewToken([LocalTokens.FinanceAgentReadRole]));

        using var response = await client.GetAsync(ViewPath(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Uri ViewPath() =>
        new($"/v1/agent/finance/carriers/{FinanceAgentCalls.Carrier}/invoices/{FinanceAgentCalls.NewInvoiceNumber()}/lines/{FinanceAgentCalls.NewReference()}", UriKind.Relative);
}
