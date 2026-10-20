using System.Net;
using System.Text.Json.Nodes;
using Norvia.Erp.Application.CarrierInvoices.List;

namespace Norvia.Erp.Api.IntegrationTests.Reset;

/// <summary>A test of the Simulator's reset at the ERP API (ADR 0039): sets up one of every kind of record the ERP keeps, and resets as the Simulator does.</summary>
public abstract class ResetTest(ErpFixture fixture) : CarrierTest(fixture)
{
    protected const string CarrierInvoiceNumber = "VDT-2026-0001";

    /// <summary>
    /// Leaves in the ERP a Shipper invoice with a Credit note, Billable lines, Carrier costs, and a Carrier invoice with a Matched line and an accepted one,
    /// once the outbox has published the events they raised, which the reset waits for. Returns the Shipper invoice's number.
    /// </summary>
    protected async Task<string> RecordOneOfEveryKindAsync()
    {
        await RecordAsync(Billable("TO-0000401", freight: 150m));
        await RecordAsync(Billable("TO-0000402", freight: 100m));
        var invoice = await InvoiceAsync(Brouwer);
        (await Finance.IssueCreditNoteAsync(invoice.InvoiceNumber, "TO-0000401", 50m, "Damaged pallets.", Ct)).Number.Should().NotBeNull();
        await RecordAsync(Billable("TO-0000403", freight: 80m));
        await RecordCostAsync("CB-0000101", "BK-A", 100m);
        await RecordCostAsync("CB-0000102", "BK-B", 80m);
        await RecordCostAsync("CB-0000103", "BK-C", 60m);
        await ReceiveAsync(CarrierInvoiceNumber, ("BK-A", 100m), ("BK-B", 95m));
        using var client = Fixture.ApiAs(ErpFixture.ManageCarrierInvoiceRole);
        using var accepted = await client.PostAsync(new Uri($"/v1/carrier-invoices/{Carrier}/{CarrierInvoiceNumber}/lines/BK-B/accept", UriKind.Relative), content: null, Ct);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(Ct));
        await Fixture.PublishedSettledAsync(Ct);
        return invoice.InvoiceNumber;
    }

    /// <summary>Resets as the Simulator's identity does.</summary>
    protected async Task<HttpResponseMessage> ResetAsync(params string[] roles)
    {
        using var client = Fixture.ApiAs(roles.Length > 0 ? roles : [ErpFixture.ResetRole]);
        return await client.PostAsync(new Uri("/v1/reset", UriKind.Relative), content: null, Ct);
    }

    /// <summary>Resets as the Simulator's identity does, which must succeed, and returns what was deleted by kind.</summary>
    protected async Task<IReadOnlyDictionary<string, int>> ResetSucceedsAsync()
    {
        using var response = await ResetAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return DeletedOf(await BodyOfAsync(response));
    }

    protected static IReadOnlyDictionary<string, int> DeletedOf(JsonObject report) =>
        report["deleted"]!.AsArray().ToDictionary(kind => kind!["kind"]!.GetValue<string>(), kind => kind!["count"]!.GetValue<int>());

    /// <summary>What the ERP holds, as Finance's lists show it, by kind.</summary>
    protected async Task<(int Invoices, int CreditNotes, int CarrierInvoices, int OpenCarrierCosts, int WaitingLines)> HeldAsync() => (
        (await InvoicesAsync()).Count,
        (await CreditNotesAsync()).Count,
        (await Finance.AskAsync(new ListCarrierInvoicesQuery(), Ct)).Count,
        (await OpenCostsAsync()).Count,
        (await WaitingLinesOfAsync(Brouwer)).Count);
}
