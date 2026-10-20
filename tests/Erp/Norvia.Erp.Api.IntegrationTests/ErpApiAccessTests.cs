using System.Net;
using System.Net.Http.Json;

namespace Norvia.Erp.Api.IntegrationTests;

/// <summary>
/// Who may call the ERP billing API (story 12 and the coding guidelines' §8): the Billing Logic App's identity records Billable lines and nothing else, the Shipper invoice
/// Logic App's reports on documents and nothing else, and a caller whose token isn't the Entra tenant's, or isn't meant for the ERP, is not let in at all.
/// </summary>
public sealed class ErpApiAccessTests(ErpFixture fixture) : ErpTest(fixture)
{
    public static TheoryData<string, string> Endpoints => new()
    {
        { "/v1/billable-lines", ErpFixture.RecordRole },
        { "/v1/shipper-invoices/INV-2026-000001/report-sent", ErpFixture.ReportRole },
        { "/v1/shipper-invoices/INV-2026-000001/report-not-delivered", ErpFixture.ReportRole },
        { "/v1/credit-notes/CN-2026-000001/report-sent", ErpFixture.ReportRole },
        { "/v1/credit-notes/CN-2026-000001/report-not-delivered", ErpFixture.ReportRole },
        { "/v1/carrier-invoices/CAR-0001/VDT-1/lines/BK-1/accept", ErpFixture.ManageCarrierInvoiceRole },
        { "/v1/carrier-invoices/CAR-0001/VDT-1/lines/BK-1/reject", ErpFixture.ManageCarrierInvoiceRole },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ACallWithoutAToken_IsRefusedWith401(string path, string role)
    {
        using var client = Fixture.ApiWith(token: null);

        using var response = await client.SendAsync(Call(path), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: $"{role} is needed");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ACallWithATokenWithoutTheRole_IsRefusedWith403(string path, string role)
    {
        var otherRole = role == ErpFixture.RecordRole ? ErpFixture.ReportRole : ErpFixture.RecordRole;

        foreach (var roles in new[] { Array.Empty<string>(), [otherRole], ["Finance"] })
        {
            using var client = Fixture.ApiAs(roles);
            using var response = await client.SendAsync(Call(path), Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because: $"{string.Join(',', roles)} isn't {role}");
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ACallWithATokenForAnotherAudienceOrSignedWithAnotherKey_IsRefusedWith401(string path, string role)
    {
        foreach (var token in new[]
        {
            ErpFixture.IssueToken([role], audience: "api://norvia-tms"),
            ErpFixture.IssueToken([role], signingKey: "0123456789abcdef0123456789abcdef"),
        })
        {
            using var client = Fixture.ApiWith(token);
            using var response = await client.SendAsync(Call(path), Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task RecordingWithTheReportRole_IsRefused_AndRecordsNothing()
    {
        using var client = Fixture.ApiAs(ErpFixture.ReportRole);

        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), Billable("TO-0000601"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Finance.AskAsync(new Application.BillableLines.ListWaiting.ListWaitingBillableLinesQuery(), Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReportingWithTheRecordRole_IsRefused_AndChangesNothing()
    {
        await RecordAsync(Brouwer, "TO-0000602");
        var invoice = await InvoiceAsync(Brouwer);

        using var response = await ReportInvoiceSentAsync(invoice.InvoiceNumber, invoice.CorrelationId, roles: [ErpFixture.RecordRole]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await OpenInvoiceAsync(invoice.InvoiceNumber)).Status.Should().Be("Issued");
    }

    [Fact]
    public async Task TheOpenApiDocument_NeedsNoToken()
    {
        using var client = Fixture.ApiWith(token: null);

        using var openApi = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        openApi.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static HttpRequestMessage Call(string path) => new(HttpMethod.Post, new Uri(path, UriKind.Relative))
    {
        Content = JsonContent.Create(new { }),
    };
}
